using Automaton.Core.Enums;
using Automaton.Core.Models;
using System.Text.Json;

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
        var volume = HostContainerResourcePolicy.VolumeName(scope);
        if (!await HostContainerResourcePolicy.EnsureVolumeAsync(scope, executor, cancellationToken))
            return Failure("trusted_storage", "The app workspace volume is missing or has invalid ownership labels.");

        var attempt = scope.DeploymentAttemptId;
        if (attempt == Guid.Empty)
            return Failure("trusted_mapping_unavailable", "A server-issued deployment attempt is required.");
        var source = $"{RunnerWorkspaceService.GetDeploymentStagingVolumeSubpath(scope.AppId, scope.DeploymentTargetId, attempt)}/source";
        var container = HostContainerBuildContainerName.For(scope);
        var existing = await InspectBuildContainerAsync(container, request, cancellationToken);
        if (existing.Failure is not null) return existing.Failure;
        if (existing.Exists)
            return await RecoverBuildAsync(request, container, existing.Image!, cancellationToken);

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
        var args = new List<string> { "docker", "run", "--name", container, "--network", "bridge",
            // The Runner image creates its non-root automaton account with uid/gid 1000.
            // Build output must remain removable by the Runner after the isolated build exits.
            "--user", "1000:1000", "--security-opt", "no-new-privileges", "--cap-drop", "ALL",
            "--label", "automaton.managed=true", "--label", "automaton.runtime-kind=build",
            "--label", $"automaton.app-id={scope.AppId:N}",
            "--label", $"automaton.deployment-target-id={scope.DeploymentTargetId:N}",
            "--label", $"automaton.deployment-attempt-id={scope.DeploymentAttemptId:N}",
            "--label", $"automaton.operation-id={request.OperationId:N}",
            "--label", $"automaton.resolved-image={image}",
            "--mount", $"type=volume,source={volume},target=/automaton/source,volume-subpath={source}" };
        if (scope.MachineRunnerId != Guid.Empty)
            args.AddRange(["--label", $"automaton.machine-runner-id={scope.MachineRunnerId:N}"]);
        else
        {
            args.AddRange(["--label", $"automaton.hosted-runner-id={scope.HostedRunnerId:N}",
                "--label", $"automaton.runner-host-id={scope.RunnerHostId:N}"]);
        }
        HostContainerResourcePolicy.AddCgroupParent(args, scope);
        HostContainerResourcePolicy.AddContainerLimits(args, request.ContainerLimits);
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
        catch (OperationCanceledException) when (buildTimeout.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            await StopTimedOutBuildAsync(container);
            return Failure("build_timeout", $"The build timed out after {request.TimeoutSeconds} seconds.");
        }

        if (result.Success)
            return new HostContainerExecutionResponse { Success = true, RequestedImage = request.RequestedImage!, ResolvedImage = image, ContainerName = container };
        if (cancellationToken.IsCancellationRequested) return Failure("build_canceled", "The build was canceled.");
        if (buildTimeout.IsCancellationRequested || result.ExitStatus == 124)
        {
            await StopTimedOutBuildAsync(container);
            return Failure("build_timeout", $"The build timed out after {request.TimeoutSeconds} seconds.");
        }
        return Failure("build_failed", result.Error);
    }

    private async Task<(bool Exists, string? Image, HostContainerExecutionResponse? Failure)> InspectBuildContainerAsync(
        string container,
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await executor(["docker", "container", "inspect", "--format", "{{json .}}", container],
            TimeSpan.FromSeconds(30), cancellationToken);
        if (!result.Success)
            return IsMissingContainer(result, container)
                ? (false, null, null)
                : (false, null, Failure("build_inspect", "Docker could not inspect the existing build operation."));
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            var labels = root.GetProperty("Config").GetProperty("Labels");
            var expected = new Dictionary<string, string>
            {
                ["automaton.operation-id"] = request.OperationId.ToString("N"),
                ["automaton.app-id"] = request.Scope.AppId.ToString("N"),
                ["automaton.deployment-target-id"] = request.Scope.DeploymentTargetId.ToString("N"),
                ["automaton.deployment-attempt-id"] = request.Scope.DeploymentAttemptId.ToString("N")
            };
            if (request.Scope.MachineRunnerId != Guid.Empty)
                expected["automaton.machine-runner-id"] = request.Scope.MachineRunnerId.ToString("N");
            else
            {
                expected["automaton.hosted-runner-id"] = request.Scope.HostedRunnerId.ToString("N");
                expected["automaton.runner-host-id"] = request.Scope.RunnerHostId.ToString("N");
            }
            if (expected.Any(pair => !labels.TryGetProperty(pair.Key, out var value) || value.GetString() != pair.Value))
                return (false, null, Failure("build_ownership", "The existing build container belongs to another operation."));
            var image = labels.GetProperty("automaton.resolved-image").GetString();
            if (string.IsNullOrWhiteSpace(image))
                return (false, null, Failure("build_inspect", "The existing build container has no resolved image."));
            return (true, image, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return (false, null, Failure("build_inspect", "Docker returned invalid existing build state."));
        }
    }

    private async Task<HostContainerExecutionResponse> RecoverBuildAsync(
        HostContainerExecutionRequest request,
        string container,
        string image,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
        InfrastructureCommandResponse wait;
        try
        {
            wait = await executor(["docker", "wait", container], TimeSpan.FromSeconds(request.TimeoutSeconds), timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            await StopTimedOutBuildAsync(container);
            return Failure("build_timeout", $"The build timed out after {request.TimeoutSeconds} seconds.");
        }
        if (cancellationToken.IsCancellationRequested)
            return Failure("build_canceled", "The build was canceled.");
        if (timeout.IsCancellationRequested || wait.ExitStatus == 124)
        {
            await StopTimedOutBuildAsync(container);
            return Failure("build_timeout", $"The build timed out after {request.TimeoutSeconds} seconds.");
        }
        if (!wait.Success || !int.TryParse(wait.Output.Trim(), out var exitCode))
            return Failure("build_recovery", "Docker could not recover the existing build operation.");
        if (exitCode == 0)
            return new HostContainerExecutionResponse { Success = true, RequestedImage = request.RequestedImage!, ResolvedImage = image, ContainerName = container };
        var logs = await executor(["docker", "logs", container], TimeSpan.FromSeconds(30), cancellationToken);
        return Failure("build_failed", logs.Output + logs.Error);
    }

    private async Task StopTimedOutBuildAsync(string container)
    {
        try
        {
            await executor(["docker", "kill", container], TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        catch
        {
            // Result delivery still retries cleanup; a callback outage must not delay this stop attempt.
        }
        // Retain the exited container so redelivery can recover its outcome before result persistence.
    }

    private static bool IsMissingContainer(InfrastructureCommandResponse result, string reference)
        => result.Error.Trim() is var error
            && (error == $"Error response from daemon: No such container: {reference}"
                || error == $"Error: No such container: {reference}"
                || error == $"Error: No such object: {reference}"
                || error == $"Error response from daemon: No such object: {reference}");

    private static bool HasTrustedResources(HostContainerResourceRef? resources)
        => resources is not null
            && resources.SourceKind == HostContainerResourceKinds.Source
            && resources.ArtifactKind == HostContainerResourceKinds.Artifact
            && resources.DataKind == HostContainerResourceKinds.ApplicationData;

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };
}
