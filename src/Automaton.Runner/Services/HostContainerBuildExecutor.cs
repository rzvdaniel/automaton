using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class HostContainerBuildExecutor(
    Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> executor)
{
    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation is not null) return validation;

        var scope = request.Scope!;
        var volume = $"automaton-hosted-runner-{scope.HostedRunnerId:N}-user";
        var labels = await executor(["docker", "volume", "inspect", "--format", "{{json .Labels}}", volume], TimeSpan.FromSeconds(900), cancellationToken);
        if (!labels.Success || !HostContainerExecutionService.HasExpectedVolumeLabels(labels.Output, scope.HostedRunnerId))
            return Failure("trusted_storage", "The Hosted Runner user volume is missing or has invalid ownership labels.");

        var attempt = scope.DeploymentAttemptId;
        if (attempt == Guid.Empty)
            return Failure("trusted_mapping_unavailable", "A server-issued deployment attempt is required.");
        var source = $"{RunnerWorkspaceService.GetDeploymentStagingVolumeSubpath(scope.AppId, scope.DeploymentTargetId, attempt)}/source";
        var container = $"automaton-build-{scope.AppId:N}-{scope.DeploymentTargetId:N}-{Guid.NewGuid():N}";
        var imageResult = await ResolveImageAsync(request.RequestedImage!, cancellationToken);
        if (imageResult.Failure is not null) return imageResult.Failure;

        var arguments = CreateArguments(request, scope, volume, source, container, imageResult.Image!);
        if (arguments.Failure is not null) return arguments.Failure;
        return await RunContainerAsync(request, cancellationToken, arguments.Arguments!, container, imageResult.Image!);
    }

    private static HostContainerExecutionResponse? ValidateRequest(HostContainerExecutionRequest request)
    {
        if (!HasTrustedResources(request.Resources))
            return Failure("trusted_mapping_unavailable", "The trusted Hosted Runner storage mapping is unavailable.");
        if (request.Command is null || request.Environment is null || request.Command.Arguments is null
            || request.Command.Environment is null || string.IsNullOrWhiteSpace(request.Command.Executable))
            return Failure("invalid_request", "RunBuild contains an invalid command or environment.");
        if (request.TimeoutSeconds is < 1 or > 86_400)
            return Failure("invalid_request", "The build timeout must be between 1 and 86400 seconds.");
        if (string.IsNullOrWhiteSpace(request.RequestedImage) || request.RequestedImage.Any(char.IsWhiteSpace)
            || request.RequestedImage.Contains('\0') || request.RequestedImage.StartsWith("-", StringComparison.Ordinal))
            return Failure("invalid_request", "RunBuild contains an invalid image reference.");
        return null;
    }

    private async Task<(string? Image, HostContainerExecutionResponse? Failure)> ResolveImageAsync(
        string requestedImage,
        CancellationToken cancellationToken)
    {
        var pull = await executor(["docker", "pull", requestedImage], TimeSpan.FromSeconds(900), cancellationToken);
        if (!pull.Success) return (null, Failure("image_pull", pull.Error));
        var inspect = await executor(["docker", "image", "inspect", "--format", "{{index .RepoDigests 0}}", requestedImage], TimeSpan.FromSeconds(900), cancellationToken);
        if (!inspect.Success) return (null, Failure("image_inspect", inspect.Error));
        var image = inspect.Output.Trim();
        return image.Contains("@sha256:", StringComparison.OrdinalIgnoreCase)
            ? (image, null)
            : (null, Failure("image_digest", "Docker did not return a repository RepoDigest."));
    }

    private static (List<string>? Arguments, HostContainerExecutionResponse? Failure) CreateArguments(
        HostContainerExecutionRequest request,
        HostContainerScope scope,
        string volume,
        string source,
        string container,
        string image)
    {
        var args = new List<string> { "docker", "run", "--rm", "--name", container, "--network", "bridge",
            "--cgroup-parent", HostedRunnerResourceGroup.GetCgroupParent(scope.HostedRunnerId),
            "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
            "--label", "automaton.managed=true", "--label", "automaton.runtime-kind=build",
            "--label", $"automaton.hosted-runner-id={scope.HostedRunnerId:N}",
            "--label", $"automaton.runner-host-id={scope.RunnerHostId:N}",
            "--label", $"automaton.app-id={scope.AppId:N}",
            "--label", $"automaton.deployment-target-id={scope.DeploymentTargetId:N}",
            "--mount", $"type=volume,source={volume},target=/automaton/source,volume-subpath={source}" };
        HostContainerWritablePathPolicy.AddTo(
            args, request.SharedMemoryMb!.Value, request.DockerLogMaxSizeMb!.Value, request.DockerLogMaxFiles!.Value);
        var command = request.Command!;
        args.Add("--workdir");
        try { args.Add(DockerContainerRuntime.ResolveContainerWorkingDirectory(command.WorkingDirectory)); }
        catch (InvalidOperationException ex) { return (null, Failure("command", ex.Message)); }
        var environment = new Dictionary<string, string>(command.Environment!, StringComparer.Ordinal);
        foreach (var pair in request.Environment!) environment[pair.Key] = pair.Value;
        environment.TryAdd("HOME", "/automaton/source/.automaton-home");
        environment.TryAdd("XDG_CACHE_HOME", "/automaton/source/.automaton-cache");
        environment.TryAdd("TMPDIR", "/tmp");
        try { DockerContainerRuntime.AddEnvironment(args, environment); }
        catch (InvalidOperationException ex) { return (null, Failure("command", ex.Message)); }
        if (string.IsNullOrWhiteSpace(command.Executable))
            return (null, Failure("command", "The build executable is required."));
        if (!IsSafeExecutable(command.Executable))
            return (null, Failure("command", "The build executable must remain inside the managed source workspace."));
        DockerContainerRuntime.AddEntrypoint(args, command, "/automaton/source");
        args.Add(image);
        DockerContainerRuntime.AddCommandArguments(args, command);
        return (args, null);
    }

    private static bool IsSafeExecutable(string executable)
        => !string.IsNullOrWhiteSpace(executable)
            && (!executable.Contains('/')
                || !Path.IsPathRooted(executable)
                    && !executable.Replace('\\', '/').Split('/').Any(part => part is "." or ".."));

    private async Task<HostContainerExecutionResponse> RunContainerAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken,
        IReadOnlyList<string> arguments,
        string container,
        string image)
    {
        using var buildTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = TimeSpan.FromSeconds(request.TimeoutSeconds);
        buildTimeout.CancelAfter(timeout);
        InfrastructureCommandResponse result;
        try
        {
            result = await executor(arguments, timeout, buildTimeout.Token);
        }
        catch
        {
            await TryCleanupContainerAsync(container);
            throw;
        }

        if (result.Success)
            return new HostContainerExecutionResponse { Success = true, RequestedImage = request.RequestedImage!, ResolvedImage = image, ContainerName = container };
        await TryCleanupContainerAsync(container);
        if (cancellationToken.IsCancellationRequested) return Failure("build_canceled", "The build was canceled.");
        if (buildTimeout.IsCancellationRequested) return Failure("build_timeout", $"The build timed out after {request.TimeoutSeconds} seconds.");
        return Failure("build_failed", result.Error);
    }

    private async Task TryCleanupContainerAsync(string container)
    {
        try
        {
            await executor(["docker", "rm", "-f", container], TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        catch
        {
            // Cleanup is best effort and must not replace the build outcome.
        }
    }

    private static bool HasTrustedResources(HostContainerResourceRef? resources)
        => resources is not null
            && resources.SourceKind == HostContainerResourceKinds.Source
            && resources.ArtifactKind == HostContainerResourceKinds.Artifact
            && resources.DataKind == HostContainerResourceKinds.ApplicationData;

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };
}
