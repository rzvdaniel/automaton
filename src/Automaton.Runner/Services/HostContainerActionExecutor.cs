using System.Text.Json;
using System.Text;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>Runs app Actions only through scoped, resource-bounded Docker jobs.</summary>
internal sealed class HostContainerActionExecutor(
    Func<IReadOnlyList<string>, TimeSpan, CancellationToken, Task<InfrastructureCommandResponse>> execute,
    RunnerWorkspaceService workspace)
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);
    private const int MaximumInputBytes = 1_048_576;

    public async Task<HostContainerExecutionResponse> ExecuteAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request);
        if (validation is not null) return validation;
        if (!await HostContainerResourcePolicy.EnsureVolumeAsync(request.Scope, execute, cancellationToken))
            return Failure("trusted_storage", "The Machine workspace volume is missing or has invalid ownership labels.");

        return await ExecuteValidatedAsync(request, cancellationToken);
    }

    private async Task<HostContainerExecutionResponse> ExecuteValidatedAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var path = request.Operation == HostContainerOperationKinds.PrepareActionDependencies
            ? workspace.GetAppActionStagingDirectory(request.Scope.AppId, request.Scope.DeploymentTargetId,
                request.ActionKey!, request.ActionStagingId)
            : workspace.GetAppActionCurrentDirectory(request.Scope.AppId, request.Scope.DeploymentTargetId, request.ActionKey!);
        var manifest = ReadManifest(path, request.ActionKey!);
        if (manifest.Failure is not null) return manifest.Failure;

        var containerName = request.Operation == HostContainerOperationKinds.ExecuteAction
            ? ActionContainerName(request)
            : PreparationContainerName(request);
        var operationTimeout = TimeSpan.FromSeconds(request.Operation == HostContainerOperationKinds.ExecuteAction
            ? request.TimeoutSeconds : 600);
        var result = await RunContainerAsync(request, manifest.Manifest!, path, containerName, operationTimeout, cancellationToken);
        if (result.Failure is not null) return result.Failure;

        if (request.Operation == HostContainerOperationKinds.PrepareActionDependencies)
            return result.ExecutionResult!.Success
                ? new() { Success = true }
                : Failure("action_preparation", "Action dependencies could not be prepared in the bounded container.");

        var executionResult = result.ExecutionResult!;
        var timedOut = executionResult.ExitStatus == 124;
        return new HostContainerExecutionResponse
        {
            Success = executionResult.Success,
            ErrorCode = timedOut ? "action_timeout" : executionResult.Success ? string.Empty : "action_exit",
            Error = timedOut ? "Action container execution exceeded its time limit." : string.Empty,
            TimedOut = timedOut,
            ExitCode = executionResult.ExitStatus,
            Output = executionResult.Output,
            OutputExceededLimit = executionResult.OutputExceededLimit,
            Logs = executionResult.ErrorTruncated ? executionResult.Error + "\n[logs truncated by Automaton]" : executionResult.Error
        };
    }

    private async Task<ContainerRunResult> RunContainerAsync(
        HostContainerExecutionRequest request,
        AppActionManifest manifest,
        string path,
        string containerName,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        var inputDirectory = request.Operation == HostContainerOperationKinds.ExecuteAction
            ? workspace.GetAppActionExecutionInputDirectory(request.Scope.AppId, request.Scope.DeploymentTargetId,
                request.ActionKey!, request.ActionExecutionId)
            : null;
        try
        {
            if (inputDirectory is not null)
                await WriteExecutionInputAsync(inputDirectory, request.ActionInputJson!, cancellationToken);
            var arguments = request.Operation == HostContainerOperationKinds.ExecuteAction
                ? CreateExecutionArguments(request, manifest, containerName)
                : CreatePreparationArguments(request, manifest, path, containerName);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(operationTimeout);
            try
            {
                return new(null, await execute(arguments, operationTimeout, timeout.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(new() { ErrorCode = "action_timeout", Error = "Action container execution exceeded its time limit.", TimedOut = true }, null);
            }
        }
        finally
        {
            await RemoveContainerAsync(containerName);
            if (inputDirectory is not null && Directory.Exists(inputDirectory))
                Directory.Delete(inputDirectory, recursive: true);
        }

    }

    public async Task<HostContainerExecutionResponse> RemoveAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!ValidMachineScope(request.Scope) || request.ActionExecutionId == Guid.Empty)
            return Failure("invalid_request", "A machine scope and Action execution identity are required.");
        await RemoveContainerAsync(ActionContainerName(request));
        return new() { Success = true };
    }

    private List<string> CreateExecutionArguments(
        HostContainerExecutionRequest request,
        AppActionManifest manifest,
        string containerName)
    {
        var app = request.Scope.AppId;
        var target = request.Scope.DeploymentTargetId;
        var arguments = NewBoundedContainerArguments(request, containerName);
        arguments.Add("--mount");
        arguments.Add(VolumeMount(HostContainerResourcePolicy.VolumeName(request.Scope),
            RunnerWorkspaceService.GetAppActionCurrentVolumeSubpath(app, target, request.ActionKey!),
            "/automaton/action", readOnly: true));
        arguments.Add("--mount");
        arguments.Add(VolumeMount(HostContainerResourcePolicy.VolumeName(request.Scope),
            RunnerWorkspaceService.GetAppActionExecutionInputVolumeSubpath(app, target, request.ActionKey!, request.ActionExecutionId),
            "/automaton/input", readOnly: true));
        arguments.Add("--workdir");
        arguments.Add("/automaton/action");
        foreach (var variable in request.Environment.OrderBy(item => item.Key, StringComparer.Ordinal))
            AddEnvironment(arguments, variable.Key, variable.Value);
        arguments.Add("--entrypoint");
        arguments.Add("/bin/sh");
        arguments.Add(request.RequestedImage!);
        arguments.Add("-c");
        arguments.Add("cat /automaton/input/input.json | exec \"$1\" \"$2\"");
        arguments.Add("automaton-action");
        arguments.Add(manifest.Spec.Runtime == AppActionRuntimes.Python
            ? "/automaton/action/.venv/bin/python"
            : "/bin/bash");
        arguments.Add("/automaton/action/" + manifest.Spec.Entrypoint.Replace('\\', '/'));
        return arguments;
    }

    private static async Task WriteExecutionInputAsync(string directory, string input, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "input.json");
        await File.WriteAllTextAsync(path, input, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private List<string> CreatePreparationArguments(
        HostContainerExecutionRequest request,
        AppActionManifest manifest,
        string stagingDirectory,
        string containerName)
    {
        var arguments = NewBoundedContainerArguments(request, containerName);
        arguments.Add("--mount");
        arguments.Add(VolumeMount(HostContainerResourcePolicy.VolumeName(request.Scope),
            RunnerWorkspaceService.GetAppActionStagingVolumeSubpath(request.Scope.AppId,
                request.Scope.DeploymentTargetId, request.ActionKey!, request.ActionStagingId),
            "/automaton/action", readOnly: false));
        arguments.Add("--workdir");
        arguments.Add("/automaton/action");
        arguments.Add("--entrypoint");
        arguments.Add("/bin/sh");
        arguments.Add(request.RequestedImage!);
        arguments.Add("-c");
        var install = File.Exists(Path.Combine(stagingDirectory, "requirements.txt"))
            ? "python -m venv .venv && .venv/bin/python -m pip install --disable-pip-version-check -r requirements.txt"
            : "python -m venv .venv";
        arguments.Add(install);
        _ = manifest;
        return arguments;
    }

    private static List<string> NewBoundedContainerArguments(HostContainerExecutionRequest request, string name)
    {
        var args = new List<string>
        {
            "docker", "run", "--rm", "--interactive", "--name", name,
            // Match the Runner-owned workspace and owner-only execution input files.
            "--user", "1000:1000",
            "--network", "bridge", "--read-only", "--security-opt", "no-new-privileges",
            "--cap-drop", "ALL", "--tmpfs", "/tmp:rw,nosuid,nodev,size=32m,mode=1777",
            "--shm-size", $"{request.SharedMemoryMb}m", "--log-driver", "local",
            "--log-opt", $"max-size={request.DockerLogMaxSizeMb}m",
            "--log-opt", $"max-file={request.DockerLogMaxFiles}"
        };
        HostContainerResourcePolicy.AddContainerLimits(args, request.ContainerLimits);
        args.Add("--label"); args.Add("automaton.managed=true");
        args.Add("--label"); args.Add("automaton.resource=action-job");
        args.Add("--label"); args.Add($"automaton.machine-runner-id={request.Scope.MachineRunnerId:N}");
        args.Add("--label"); args.Add($"automaton.app-id={request.Scope.AppId:N}");
        args.Add("--label"); args.Add($"automaton.deployment-target-id={request.Scope.DeploymentTargetId:N}");
        args.Add("--label"); args.Add($"automaton.action-key={request.ActionKey}");
        args.Add("--label"); args.Add($"automaton.action-execution-id={request.ActionExecutionId:N}");
        return args;
    }

    private static HostContainerExecutionResponse? Validate(HostContainerExecutionRequest request)
        => ValidateMachinePolicy(request)
            ?? ValidateImage(request)
            ?? ValidateActionKey(request)
            ?? ValidatePayload(request);

    private static HostContainerExecutionResponse? ValidateMachinePolicy(HostContainerExecutionRequest request)
        => ValidMachineScope(request.Scope) && request.ActionExecutionId != Guid.Empty
            && request.ContainerLimits?.IsValid() == true
            && request.SharedMemoryMb is >= 16 and <= 1024
            && request.DockerLogMaxSizeMb is >= 1 and <= 1024
            && request.DockerLogMaxFiles is >= 1 and <= 20
                ? null
                : Failure("policy", "A machine Action requires a complete scope and server-derived container limits.");

    private static HostContainerExecutionResponse? ValidateImage(HostContainerExecutionRequest request)
        => !string.IsNullOrWhiteSpace(request.RequestedImage)
            && !request.RequestedImage.Any(char.IsWhiteSpace)
            && !request.RequestedImage.Contains('\0')
            && !request.RequestedImage.StartsWith("-", StringComparison.Ordinal)
                ? null
                : Failure("image", "A valid server-selected Action image is required.");

    private static HostContainerExecutionResponse? ValidateActionKey(HostContainerExecutionRequest request)
    {
        try { AppActionManifestParser.ValidateKey(request.ActionKey ?? string.Empty); }
        catch (InvalidOperationException ex) { return Failure("action_key", ex.Message); }
        return null;
    }

    private static HostContainerExecutionResponse? ValidatePayload(HostContainerExecutionRequest request)
        => request.Operation switch
        {
            HostContainerOperationKinds.ExecuteAction => ValidateExecutionPayload(request),
            HostContainerOperationKinds.PrepareActionDependencies => ValidatePreparationPayload(request),
            _ => Failure("operation", "The Action container operation is not supported.")
        };

    private static HostContainerExecutionResponse? ValidateExecutionPayload(HostContainerExecutionRequest request)
        => request.ActionStagingId == Guid.Empty && request.ActionInputJson is not null
            && System.Text.Encoding.UTF8.GetByteCount(request.ActionInputJson) <= MaximumInputBytes
            && request.TimeoutSeconds is >= 1 and <= 86_400 && request.Environment is not null
                ? null
                : Failure("invalid_request", "Action execution input, timeout or environment is invalid.");

    private static HostContainerExecutionResponse? ValidatePreparationPayload(HostContainerExecutionRequest request)
        => request.ActionStagingId != Guid.Empty && request.ActionInputJson is null && request.Environment is { Count: 0 }
            ? null
            : Failure("invalid_request", "Action dependency preparation requires a staging identity only.");

    private (AppActionManifest? Manifest, HostContainerExecutionResponse? Failure) ReadManifest(string directory, string key)
    {
        var path = Path.Combine(directory, "action.yaml");
        if (!File.Exists(path)) return (null, Failure("action_missing", $"Action '{key}' is not deployed."));
        try
        {
            var manifest = AppActionManifestParser.ParseFile(path, key, allowPrivilegedCapabilities: false);
            if (manifest.Spec.Runtime is not (AppActionRuntimes.Python or AppActionRuntimes.Bash))
                return (null, Failure("action_runtime", "The Action runtime is not supported by the bounded job container."));
            return (manifest, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
        {
            return (null, Failure("action_manifest", "The deployed Action manifest is invalid."));
        }
    }

    private async Task RemoveContainerAsync(string name)
    {
        try { await execute(["docker", "rm", "-f", name], CleanupTimeout, CancellationToken.None); }
        catch { /* Cleanup is best effort after the bounded process has exited or been interrupted. */ }
    }

    private static bool ValidMachineScope(HostContainerScope scope)
        => scope.MachineRunnerId != Guid.Empty && scope.AppId != Guid.Empty && scope.DeploymentTargetId != Guid.Empty
            && scope.HostedRunnerId == Guid.Empty && scope.RunnerHostId == Guid.Empty;

    private static string ActionContainerName(HostContainerExecutionRequest request)
        => $"automaton-action-{request.Scope.MachineRunnerId:N}-{request.ActionExecutionId:N}";

    private static string PreparationContainerName(HostContainerExecutionRequest request)
        => $"automaton-action-prepare-{request.Scope.MachineRunnerId:N}-{request.ActionExecutionId:N}";

    private static string VolumeMount(string volume, string subpath, string target, bool readOnly)
        => string.Join(',', new[] { "type=volume", $"source={volume}", $"target={target}", $"volume-subpath={subpath}", "volume-nocopy" }
            .Concat(readOnly ? new[] { "readonly" } : [])
            .Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static void AddEnvironment(List<string> arguments, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_'))
            || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || value.Contains('\0'))
            throw new InvalidOperationException("The Action environment contains an invalid variable.");
        arguments.Add("--env");
        arguments.Add($"{name}={value}");
    }

    private static HostContainerExecutionResponse Failure(string code, string error)
        => new() { ErrorCode = code, Error = error };

    private sealed record ContainerRunResult(HostContainerExecutionResponse? Failure, InfrastructureCommandResponse? ExecutionResult);
}
