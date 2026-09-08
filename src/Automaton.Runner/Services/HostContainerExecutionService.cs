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

        // Stage 4B1 intentionally has no trusted resource resolver. Caller
        // supplied identities do not establish a host mapping.
        return Failure("trusted_mapping_unavailable", "The trusted workspace/resource mapping is not enabled yet.");

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

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };
}
