using System.Diagnostics;
using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Typed host-container boundary for the trusted Infrastructure Runner.
/// This service deliberately does not accept Docker arguments, host paths, or
/// network names from a caller.
/// </summary>
public sealed partial class HostContainerExecutionService
{
    private readonly Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> executor;
    private readonly HostContainerBuildExecutor buildExecutor;

    public HostContainerExecutionService(InfrastructureCommandService infrastructureCommandService)
        : this((arguments, timeout, cancellationToken) => infrastructureCommandService.ExecuteTypedDockerAsync(arguments, timeout, cancellationToken))
    {
    }

    internal HostContainerExecutionService(
        Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> executor)
    {
        this.executor = executor;
        buildExecutor = new HostContainerBuildExecutor(executor);
    }

    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationFailure = ValidateRequest(request);
        if (validationFailure is not null) return validationFailure;

        if ((request.Operation is HostContainerOperationKinds.RunBuild or HostContainerOperationKinds.CreateApplicationContainer)
            && !HasTrustedResources(request.Resources))
            return Failure("trusted_mapping_unavailable", "The trusted Hosted Runner storage mapping is unavailable.");

        var runtimeLimitsFailure = ValidateRuntimeLimits(request);
        if (runtimeLimitsFailure is not null) return runtimeLimitsFailure;

        if (request.Operation == HostContainerOperationKinds.ResolveImage)
            return await ResolveImageAsync(request, cancellationToken);

        if (request.Operation == HostContainerOperationKinds.RunBuild)
            return await buildExecutor.ExecuteAsync(request, cancellationToken);

        var error = HostContainerRuntimeValidation.Validate(request);
        if (error is not null)
            return Failure("invalid_request", error);
        if (request.Operation == HostContainerOperationKinds.RemoveTargetContainers)
            return await new HostContainerRuntimeCleanup(executor, ExecuteContainerOperationAsync)
                .ExecuteAsync(request, RuntimeLabels(request.Scope), cancellationToken);
        return request.Operation == HostContainerOperationKinds.CreateApplicationContainer
            ? await CreateApplicationContainerAsync(request, cancellationToken)
            : await ExecuteContainerOperationAsync(request, cancellationToken);

    }

    private static HostContainerExecutionResponse? ValidateRequest(HostContainerExecutionRequest request)
        => HostContainerRunnerAuthorization.Validate()
            ?? HostContainerRunnerAuthorization.ValidateScope(request)
            ?? ValidateOperation(request)
            ?? ValidateRequestedImage(request);

    private static HostContainerExecutionResponse? ValidateRuntimeLimits(HostContainerExecutionRequest request)
    {
        if (request.Operation is not (HostContainerOperationKinds.RunBuild or HostContainerOperationKinds.CreateApplicationContainer))
            return null;

        return request.SharedMemoryMb is >= 16 and <= 1024
            && request.DockerLogMaxSizeMb is >= 1 and <= 1024
            && request.DockerLogMaxFiles is >= 1 and <= 20
                ? null
                : Failure("trusted_policy_unavailable", "Valid Hosted Runner container limits are unavailable.");
    }

    private static void AddWritablePathArguments(
        List<string> arguments,
        int sharedMemoryMb,
        int dockerLogMaxSizeMb,
        int dockerLogMaxFiles)
    {
        arguments.Add("--read-only");
        arguments.Add("--tmpfs");
        arguments.Add("/tmp:rw,nosuid,nodev,size=32m,mode=1777");
        arguments.Add("--shm-size");
        arguments.Add($"{sharedMemoryMb}m");
        arguments.Add("--log-driver");
        arguments.Add("local");
        arguments.Add("--log-opt");
        arguments.Add($"max-size={dockerLogMaxSizeMb}m");
        arguments.Add("--log-opt");
        arguments.Add($"max-file={dockerLogMaxFiles}");
    }

    private static HostContainerExecutionResponse? ValidateOperation(HostContainerExecutionRequest request)
        => request.Operation is HostContainerOperationKinds.ResolveImage
            or HostContainerOperationKinds.RunBuild
            or HostContainerOperationKinds.CreateApplicationContainer
            or HostContainerOperationKinds.StartContainer
            or HostContainerOperationKinds.InspectContainer
            or HostContainerOperationKinds.GetContainerLogs
            or HostContainerOperationKinds.RemoveContainer
            or HostContainerOperationKinds.RemoveTargetContainers
                ? null
                : Failure("operation", "The host-container operation is not supported.");

    private static HostContainerExecutionResponse? ValidateRequestedImage(HostContainerExecutionRequest request)
        => string.IsNullOrWhiteSpace(request.RequestedImage)
            && request.Operation is HostContainerOperationKinds.ResolveImage
                or HostContainerOperationKinds.RunBuild
                or HostContainerOperationKinds.CreateApplicationContainer
            ? Failure("image", "An image reference is required.")
            : null;

    private async Task<HostContainerExecutionResponse> ResolveImageAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var image = request.RequestedImage!;
        if (image.Any(char.IsWhiteSpace) || image.Contains('\0'))
            return Failure("image", "The image reference is invalid.");

        var pull = await executor(["docker", "pull", image], TimeSpan.FromSeconds(900), cancellationToken);
        if (!pull.Success)
            return Failure("image_pull", pull.Error);

        var inspect = await executor(["docker", "image", "inspect", "--format", "{{index .RepoDigests 0}}", image], TimeSpan.FromSeconds(900), cancellationToken);
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

    private static bool HasTrustedResources(HostContainerResourceRef? resources)
        => resources is not null
            && resources.SourceKind == HostContainerResourceKinds.Source
            && resources.ArtifactKind == HostContainerResourceKinds.Artifact
            && resources.DataKind == HostContainerResourceKinds.ApplicationData;

    internal static bool HasExpectedVolumeLabels(string json, Guid hostedRunnerId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicateProperties(document.RootElement);
            var labels = JsonSerializer.Deserialize<Dictionary<string, string>>(document.RootElement);
            return labels is not null
                && labels.Count == 4
                && labels.TryGetValue("automaton.managed", out var managed) && managed == "true"
                && labels.TryGetValue("automaton.resource", out var resource) && resource == "hosted-runner-volume"
                && labels.TryGetValue("automaton.hosted-runner-id", out var runner) && runner == hostedRunnerId.ToString()
                && labels.TryGetValue("automaton.storage-scope", out var scope) && scope == "user";
        }
        catch (JsonException) { return false; }
    }

    internal static bool HasExpectedVolumeLabels(string json, HostContainerScope scope)
        => scope.MachineRunnerId == Guid.Empty
            ? HasExpectedVolumeLabels(json, scope.HostedRunnerId)
            : HostContainerResourcePolicy.HasExpectedVolumeLabels(json, scope);

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };
}
