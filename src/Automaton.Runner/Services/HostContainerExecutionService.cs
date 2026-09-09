using System.Diagnostics;
using System.Text.Json;
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
        if (!labels.Success || !HasExpectedVolumeLabels(labels.Output, scope.HostedRunnerId))
            return Failure("trusted_storage", "The Hosted Runner user volume is missing or has invalid ownership labels.");

        var attempt = scope.DeploymentAttemptId;
        if (attempt == Guid.Empty)
            return Failure("trusted_mapping_unavailable", "A server-issued deployment attempt is required.");
        var relativeTarget = RunnerWorkspaceService.GetDeploymentStagingRelativePath(scope.AppId, scope.DeploymentTargetId, attempt);
        var source = $"{relativeTarget}/source";
        var operationId = Guid.NewGuid().ToString("N");
        var container = $"automaton-build-{scope.AppId:N}-{scope.DeploymentTargetId:N}-{operationId}";

        var pull = await executor(["docker", "pull", request.RequestedImage!], cancellationToken);
        if (!pull.Success) return Failure("image_pull", pull.Error);
        var inspect = await executor(["docker", "image", "inspect", "--format", "{{index .RepoDigests 0}}", request.RequestedImage!], cancellationToken);
        if (!inspect.Success) return Failure("image_inspect", inspect.Error);
        var image = inspect.Output.Trim();
        if (!image.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
            return Failure("image_digest", "Docker did not return a repository RepoDigest.");

        var args = new List<string> { "docker", "run", "--rm", "--name", container, "--network", "bridge",
            "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
            "--label", "automaton.managed=true", "--label", "automaton.runtime-kind=build",
            "--label", $"automaton.hosted-runner-id={scope.HostedRunnerId:N}",
            "--label", $"automaton.runner-host-id={scope.RunnerHostId:N}",
            "--label", $"automaton.app-id={scope.AppId:N}",
            "--label", $"automaton.deployment-target-id={scope.DeploymentTargetId:N}",
            // This is the isolated incoming attempt workspace, not the
            // durable accepted source. Build tools may write generated files
            // here; promotion remains owned by RunnerDeploymentService.
            "--mount", $"type=volume,source={volume},target=/automaton/source,volume-subpath={source}" };
        var command = request.Command!;
        args.Add("--workdir");
        try { args.Add(DockerContainerRuntime.ResolveContainerWorkingDirectory(command.WorkingDirectory)); }
        catch (InvalidOperationException ex) { return Failure("command", ex.Message); }
        var environment = new Dictionary<string, string>(command.Environment, StringComparer.Ordinal);
        foreach (var pair in request.Environment)
            environment[pair.Key] = pair.Value;
        try { DockerContainerRuntime.AddEnvironment(args, environment); }
        catch (InvalidOperationException ex) { return Failure("command", ex.Message); }
        if (string.IsNullOrWhiteSpace(command.Executable))
            return Failure("command", "The build executable is required.");
        if (command.Executable.Contains('/')
            && (Path.IsPathRooted(command.Executable)
                || command.Executable.Replace('\\', '/').Split('/').Any(part => part is "." or "..")))
            return Failure("command", "The build executable must remain inside the managed source workspace.");
        DockerContainerRuntime.AddEntrypoint(args, command, "/automaton/source");
        DockerContainerRuntime.AddCommandArguments(args, command);
        // Docker options and the explicit entrypoint must precede the image;
        // command arguments follow the image.
        var entrypointArguments = args.Skip(args.IndexOf("--entrypoint") + 2).ToList();
        args.RemoveRange(args.IndexOf("--entrypoint"), args.Count - args.IndexOf("--entrypoint"));
        args.Add(image);
        args.AddRange(entrypointArguments);
        using var buildTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        buildTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 1, 86_400)));
        var result = await executor(args, buildTimeout.Token);
        if (result.Success)
            return new HostContainerExecutionResponse { Success = true, RequestedImage = request.RequestedImage!, ResolvedImage = image, ContainerName = container };
        if (cancellationToken.IsCancellationRequested)
            return Failure("build_canceled", "The build was canceled.");
        if (buildTimeout.IsCancellationRequested)
            return Failure("build_timeout", $"The build timed out after {request.TimeoutSeconds} seconds.");
        return Failure("build_failed", result.Error);
    }

    private static bool HasTrustedResources(HostContainerResourceRef? resources)
        => resources is not null
            && resources.SourceKind == HostContainerResourceKinds.Source
            && resources.ArtifactKind == HostContainerResourceKinds.Artifact
            && resources.DataKind == HostContainerResourceKinds.ApplicationData;

    private static bool HasExpectedVolumeLabels(string json, Guid hostedRunnerId)
    {
        try
        {
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return labels is not null
                && labels.Count == 4
                && labels.TryGetValue("automaton.managed", out var managed) && managed == "true"
                && labels.TryGetValue("automaton.resource", out var resource) && resource == "hosted-runner-volume"
                && labels.TryGetValue("automaton.hosted-runner-id", out var runner) && runner == hostedRunnerId.ToString()
                && labels.TryGetValue("automaton.storage-scope", out var scope) && scope == "user";
        }
        catch (JsonException) { return false; }
    }

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };
}
