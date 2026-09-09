using System.Diagnostics;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Typed host-container boundary for the trusted Infrastructure Runner.
/// This service deliberately does not accept Docker arguments, host paths, or
/// network names from a caller. Stage 4B2 supplies the trusted resource map.
/// </summary>
public sealed class HostContainerExecutionService
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<InfrastructureCommandResponse>> executor;

    public HostContainerExecutionService(InfrastructureCommandService infrastructureCommandService)
        : this((arguments, cancellationToken) => infrastructureCommandService.ExecuteTypedDockerAsync(arguments, cancellationToken))
    {
    }

    internal HostContainerExecutionService(
        Func<IReadOnlyList<string>, CancellationToken, Task<InfrastructureCommandResponse>> executor)
    {
        this.executor = executor;
    }

    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"),
                RunnerPurposes.Infrastructure,
                StringComparison.Ordinal))
            return Failure("authorization", "Host container execution requires an Infrastructure Runner.");

        if (!RunnerCapabilities.Normalize(
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                RunnerPurposes.Infrastructure)
            .Contains(RunnerCapabilities.HostContainerExecution, StringComparer.OrdinalIgnoreCase))
            return Failure("capability", "Host container execution is not enabled for this Runner.");

        if (request.Scope is null || request.Scope.HostedRunnerId == Guid.Empty || request.Scope.RunnerHostId == Guid.Empty
            || request.Scope.AppId == Guid.Empty || request.Scope.DeploymentTargetId == Guid.Empty)
            return Failure("scope", "A complete host-container scope is required.");

        if (request.Operation is not (HostContainerOperationKinds.ResolveImage
            or HostContainerOperationKinds.RunBuild
            or HostContainerOperationKinds.CreateApplicationContainer
            or HostContainerOperationKinds.StartContainer
            or HostContainerOperationKinds.InspectContainer
            or HostContainerOperationKinds.GetContainerLogs
            or HostContainerOperationKinds.RemoveContainer))
            return Failure("operation", "The host-container operation is not supported.");

        if (string.IsNullOrWhiteSpace(request.RequestedImage)
            && (request.Operation is HostContainerOperationKinds.ResolveImage
                or HostContainerOperationKinds.RunBuild
                or HostContainerOperationKinds.CreateApplicationContainer))
            return Failure("image", "An image reference is required.");

        if (request.Operation == HostContainerOperationKinds.ResolveImage)
            return await ResolveImageAsync(request, cancellationToken);

        if (request.Operation == HostContainerOperationKinds.RunBuild)
            return await RunBuildAsync(request, cancellationToken);

        return Failure("trusted_mapping_unavailable", "The requested host-container operation is not enabled yet.");

    }

    private async Task<HostContainerExecutionResponse> ResolveImageAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var image = request.RequestedImage!;
        if (image.Any(char.IsWhiteSpace) || image.Contains('\0'))
            return Failure("image", "The image reference is invalid.");

        var pull = await executor(["docker", "pull", image], cancellationToken);
        if (!pull.Success)
            return Failure("image_pull", pull.Error);

        var inspect = await executor(["docker", "image", "inspect", "--format", "{{index .RepoDigests 0}}", image], cancellationToken);
        if (!inspect.Success)
            return Failure("image_inspect", inspect.Error);

        var resolved = inspect.Output.Trim();
        if (string.IsNullOrWhiteSpace(resolved) || !resolved.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
            return Failure("image_digest", "Docker did not return a repository RepoDigest.");

        return new HostContainerExecutionResponse
        {
            Success = true,
            RequestedImage = image,
            ResolvedImage = resolved
        };
    }

    private async Task<HostContainerExecutionResponse> RunBuildAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasTrustedResources(request.Resources))
            return Failure("trusted_mapping_unavailable", "The trusted Hosted Runner storage mapping is unavailable.");

        var scope = request.Scope;
        var volume = $"automaton-hosted-runner-{scope.HostedRunnerId:N}-user";
        var labels = await executor(["docker", "volume", "inspect", "--format", "{{json .Labels}}", volume], cancellationToken);
        if (!labels.Success
            || !ContainsLabel(labels.Output, "automaton.managed", "true")
            || !ContainsLabel(labels.Output, "automaton.resource", "hosted-runner-volume")
            || !ContainsLabel(labels.Output, "automaton.hosted-runner-id", scope.HostedRunnerId.ToString())
            || !ContainsLabel(labels.Output, "automaton.storage-scope", "user"))
            return Failure("trusted_storage", "The Hosted Runner user volume is missing or has invalid ownership labels.");

        var relativeTarget = $"generic-runtime/apps/{scope.AppId:N}/targets/{scope.DeploymentTargetId:N}";
        var source = $"{relativeTarget}/source";
        var artifact = $"{relativeTarget}/artifact";
        var operationId = Guid.NewGuid().ToString("N");
        var container = $"automaton-build-{scope.AppId:N}-{scope.DeploymentTargetId:N}-{operationId}";

        var ensure = await executor([
            "docker", "run", "--rm", "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
            "--mount", $"type=volume,source={volume},target=/volume",
            "--entrypoint", "/bin/sh", "busybox:1.36", "-c",
            $"mkdir -p /volume/{source} /volume/{artifact}"], cancellationToken);
        if (!ensure.Success)
            return Failure("trusted_storage", ensure.Error);

        var pull = await executor(["docker", "pull", request.RequestedImage!], cancellationToken);
        if (!pull.Success) return Failure("image_pull", pull.Error);
        var inspect = await executor(["docker", "image", "inspect", "--format", "{{index .RepoDigests 0}}", request.RequestedImage!], cancellationToken);
        if (!inspect.Success) return Failure("image_inspect", inspect.Error);
        var image = inspect.Output.Trim();
        if (!image.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
            return Failure("image_digest", "Docker did not return a repository RepoDigest.");

        var args = new List<string> { "run", "--rm", "--name", container, "--network", "bridge",
            "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
            "--label", "automaton.managed=true", "--label", "automaton.runtime-kind=build",
            "--label", $"automaton.hosted-runner-id={scope.HostedRunnerId:N}",
            "--label", $"automaton.runner-host-id={scope.RunnerHostId:N}",
            "--label", $"automaton.app-id={scope.AppId:N}",
            "--label", $"automaton.deployment-target-id={scope.DeploymentTargetId:N}",
            "--mount", $"type=volume,source={volume},target=/automaton/source,volume-subpath={source}",
            "--mount", $"type=volume,source={volume},target=/automaton/artifact,volume-subpath={artifact}" };
        foreach (var pair in request.Environment)
        { args.Add("--env"); args.Add($"{pair.Key}={pair.Value}"); }
        args.Add(image);
        args.Add(request.Command!.Executable);
        args.AddRange(request.Command.Arguments);
        var result = await executor(args, cancellationToken);
        return result.Success
            ? new HostContainerExecutionResponse { Success = true, RequestedImage = request.RequestedImage!, ResolvedImage = image, ContainerName = container }
            : Failure("build_failed", result.Error);
    }

    private static bool HasTrustedResources(HostContainerResourceRef? resources)
        => resources is not null
            && resources.SourceKind == HostContainerResourceKinds.Source
            && resources.ArtifactKind == HostContainerResourceKinds.Artifact
            && resources.DataKind == HostContainerResourceKinds.ApplicationData;

    private static bool ContainsLabel(string json, string key, string value)
        => json.Contains($"\"{key}\":\"{value}\"", StringComparison.Ordinal)
            || json.Contains($"\"{key}\": \"{value}\"", StringComparison.Ordinal);

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };
}
