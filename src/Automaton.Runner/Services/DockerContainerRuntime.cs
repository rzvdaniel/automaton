using System.Diagnostics;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed record DockerImageIdentity(string RequestedReference, string ResolvedReference);

public sealed record DockerBuildRequest(
    string Image,
    ApplicationCommandSpec Command,
    string SourceRoot,
    int TimeoutSeconds,
    RunnerApplicationBuildContext? Context = null);

public sealed record RunnerApplicationBuildContext(Guid AppId, Guid DeploymentTargetId, Guid ExecutionAttemptId);
public sealed record ApplicationContainerScope(Guid AppId, Guid DeploymentTargetId);
public sealed record ApplicationContainerStartResult(bool Running, string HostAddress, int HostPort, int ContainerPort);

public sealed record DockerApplicationContainerRequest(
    string Name,
    string Image,
    string ArtifactRoot,
    string WorkingDirectory,
    ApplicationCommandSpec Command,
    int ContainerPort,
    int HostPort,
    string DataDirectory,
    IReadOnlyDictionary<string, string> StorageMounts,
    Guid AppId,
    Guid DeploymentTargetId,
    Guid CandidateId = default,
    ApplicationStorageSpec? Storage = null,
    string? User = null,
    IReadOnlyDictionary<string, string>? Secrets = null);

public sealed record DockerApplicationContainer(
    string Id,
    string Name,
    DockerImageIdentity Image,
    int? HostPort = null);

public sealed record DockerApplicationComposeRequest(
    Guid AppId,
    Guid DeploymentTargetId,
    Guid CandidateId,
    ApplicationComposeSpec Compose,
    Guid? DeploymentAttemptId = null);

public sealed record DockerContainerIdentity(
    string Id,
    bool Exists,
    bool Running,
    IReadOnlyDictionary<string, string> Labels,
    string? HostAddress = null,
    int? HostPort = null,
    int? ContainerPort = null);

/// <summary>
/// Translates only the explicitly configured Runner workspace mount from the
/// Runner's view to the host Docker daemon's view.
/// </summary>
public sealed class RunnerManagedHostPathTranslator
{
    private readonly string runnerRoot;
    private readonly string hostRoot;

    public RunnerManagedHostPathTranslator(string runnerRoot, string hostRoot)
    {
        this.runnerRoot = NormalizeRoot(runnerRoot);
        this.hostRoot = NormalizeRoot(hostRoot);
    }

    public string Translate(string path)
    {
        var fullPath = path.StartsWith("/", StringComparison.Ordinal)
            ? path.Replace('\\', '/')
            : Path.GetFullPath(path).Replace('\\', '/');
        if (!fullPath.Equals(runnerRoot, StringComparison.Ordinal)
            && !fullPath.StartsWith(runnerRoot + "/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Docker bind source is outside the configured Runner workspace.");
        var relative = fullPath.Equals(runnerRoot, StringComparison.Ordinal)
            ? string.Empty
            : fullPath[(runnerRoot.Length + 1)..];
        return string.IsNullOrEmpty(relative)
            ? hostRoot
            : hostRoot + "/" + relative;
    }

    private static string NormalizeRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A managed path root is required.", nameof(value));
        return (value.StartsWith("/", StringComparison.Ordinal)
                ? value
                : Path.GetFullPath(value))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/')
            .Replace('\\', '/');
    }
}

/// <summary>
/// The single Docker boundary used by managed application builds and runtime
/// containers. Application commands remain opaque to this abstraction.
/// </summary>
public interface IApplicationContainerRuntime
{
    Task<DockerImageIdentity> ResolveImageAsync(string image, CancellationToken cancellationToken = default);
    Task RunBuildAsync(DockerBuildRequest request, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default);
    Task<DockerApplicationContainer> StartApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default);
    Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default);
    Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default);
    Task<DockerContainerIdentity> InspectAsync(string containerIdOrName, CancellationToken cancellationToken = default);
    Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default);
    Task<DockerApplicationContainer> CreateComposeProjectAsync(Guid appId, Guid deploymentTargetId, Guid candidateId, ApplicationContract contract, Guid? deploymentAttemptId = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This application container runtime does not support managed Compose projects.");
    Task<DockerApplicationContainer?> InspectComposeProjectAsync(Guid appId, Guid deploymentTargetId, Guid candidateId, ApplicationContract contract, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This application container runtime does not support managed Compose projects.");
    Task RemoveComposeProjectAsync(ApplicationContainerScope scope, Guid candidateId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This application container runtime does not support managed Compose projects.");
}

public interface IStagedApplicationContainerRuntime
{
    Task<DockerApplicationContainer> CreateApplicationAsync(DockerApplicationContainerRequest request, CancellationToken cancellationToken = default);
    Task StartApplicationAsync(string containerIdOrName, CancellationToken cancellationToken = default);
}

public interface IScopedApplicationContainerRuntime
{
    Task<ApplicationContainerStartResult> StartApplicationAsync(ApplicationContainerScope scope, string containerIdOrName, CancellationToken cancellationToken = default);
    Task<DockerContainerIdentity> InspectAsync(ApplicationContainerScope scope, string containerIdOrName, CancellationToken cancellationToken = default);
    Task<bool> IsRunningAsync(ApplicationContainerScope scope, string containerIdOrName, CancellationToken cancellationToken = default);
    Task<string> GetLogsAsync(ApplicationContainerScope scope, string containerIdOrName, CancellationToken cancellationToken = default);
    Task StopAndRemoveAsync(ApplicationContainerScope scope, string containerIdOrName, CancellationToken cancellationToken = default);
}

public sealed class DockerContainerRuntime : IApplicationContainerRuntime, IStagedApplicationContainerRuntime, IApplicationContainerCleanupRuntime
{
    private readonly string dockerExecutable;
    private readonly DockerCommandExecutor? commandExecutor;

    public DockerContainerRuntime(string dockerExecutable = "docker")
    {
        this.dockerExecutable = dockerExecutable;
    }

    internal DockerContainerRuntime(DockerCommandExecutor commandExecutor)
    {
        dockerExecutable = "docker";
        this.commandExecutor = commandExecutor;
    }

    public async Task<DockerImageIdentity> ResolveImageAsync(
        string image,
        CancellationToken cancellationToken = default)
    {
        ValidateImage(image);
        await ExecuteAsync(["pull", image], TimeSpan.FromMinutes(15), cancellationToken);
        var inspect = await ExecuteAsync(
            ["image", "inspect", "--format", "{{index .RepoDigests 0}}", image],
            TimeSpan.FromSeconds(30),
            cancellationToken);
        var resolved = inspect.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(resolved) || !resolved.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Docker image '{image}' did not resolve to an immutable digest.");
        return new DockerImageIdentity(image, resolved);
    }

    public async Task RunBuildAsync(
        DockerBuildRequest request,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default)
    {
        var image = await ResolveImageAsync(request.Image, cancellationToken);
        var containerName = $"automaton-build-{Guid.NewGuid():N}";
        var args = new List<string>
        {
            "run", "--network", "bridge",
            "--name", containerName,
            "--label", "com.automaton.managed=true",
            "--label", "com.automaton.runtime-kind=build",
            "--mount", $"type=bind,source={MapHostPath(request.SourceRoot)},target=/automaton/source",
            "--workdir", ResolveContainerWorkingDirectory(request.Command.WorkingDirectory),
            "--security-opt", "no-new-privileges",
            "--cap-drop", "ALL"
        };
        AddEntrypoint(args, request.Command, "/automaton/source");
        AddEnvironment(args, environment);
        args.Add(image.ResolvedReference);
        AddCommandArguments(args, request.Command);
        try
        {
            await ExecuteAsync(args, TimeSpan.FromSeconds(request.TimeoutSeconds), cancellationToken);
        }
        catch (Exception primaryError)
        {
            try { await StopAndRemoveAsync(containerName, CancellationToken.None); }
            catch (Exception cleanupError) { throw new AggregateException("Docker build failed and cleanup also failed.", primaryError, cleanupError); }
            throw;
        }
        await StopAndRemoveAsync(containerName, CancellationToken.None);
    }

    public async Task<DockerApplicationContainer> CreateApplicationAsync(
        DockerApplicationContainerRequest request,
        CancellationToken cancellationToken = default)
    {
        var image = await ResolveImageAsync(request.Image, cancellationToken);
        var network = ResolveApplicationNetwork();
        var args = new List<string>
        {
            "create", "--name", request.Name,
            "--label", $"com.automaton.managed=true",
            "--label", $"com.automaton.app-id={request.AppId}",
            "--label", $"com.automaton.deployment-target-id={request.DeploymentTargetId}",
            "--label", "com.automaton.runtime-kind=generic",
            "--network", network,
            "--mount", $"type=bind,source={MapHostPath(request.ArtifactRoot)},target=/automaton/app,readonly",
            "--mount", $"type=bind,source={MapHostPath(request.DataDirectory)},target=/automaton/data",
            "--workdir", ResolveContainerWorkingDirectory(request.WorkingDirectory, "/automaton/app"),
            "--security-opt", "no-new-privileges",
            "--cap-drop", "ALL"
        };
        if (request.User is not null)
        {
            args.Add("--user");
            args.Add(request.User);
        }
        if (network == "bridge")
        {
            args.Add("--publish");
            args.Add($"{RunnerRuntimeEndpointConfiguration.HostAddress}:{request.HostPort}:{request.ContainerPort}");
        }

        foreach (var mount in request.StorageMounts)
        {
            args.Add("--mount");
            args.Add($"type=bind,source={MapHostPath(mount.Value)},target=/automaton/data/{mount.Key}");
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AUTOMATON_PORT"] = request.ContainerPort.ToString(),
            ["AUTOMATON_APP_DATA_DIR"] = "/automaton/data",
            ["AUTOMATON_APP_ID"] = request.AppId.ToString(),
            ["AUTOMATON_DEPLOYMENT_TARGET_ID"] = request.DeploymentTargetId.ToString()
        };
        foreach (var pair in request.Command.Environment)
            environment[pair.Key] = pair.Value;
        foreach (var pair in request.Secrets ?? new Dictionary<string, string>())
            if (!pair.Key.StartsWith("AUTOMATON_", StringComparison.Ordinal)) environment[pair.Key] = pair.Value;
        foreach (var mount in request.StorageMounts)
            environment[$"AUTOMATON_STORAGE_{mount.Key.ToUpperInvariant()}"] = $"/automaton/data/{mount.Key}";
        AddEnvironment(args, environment);

        AddEntrypoint(args, request.Command, "/automaton/app");
        args.Add(image.ResolvedReference);
        AddCommandArguments(args, request.Command);
        var created = await ExecuteAsync(args, TimeSpan.FromSeconds(30), cancellationToken);
        var id = created.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Docker did not return an application container ID.");
        return new DockerApplicationContainer(id, request.Name, image);
    }

    public Task StartApplicationAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => ExecuteAsync(["start", containerIdOrName], TimeSpan.FromSeconds(30), cancellationToken);

    public async Task<DockerApplicationContainer> StartApplicationAsync(
        DockerApplicationContainerRequest request,
        CancellationToken cancellationToken = default)
    {
        var created = await CreateApplicationAsync(request, cancellationToken);
        try
        {
            await StartApplicationAsync(created.Id, cancellationToken);
            return created;
        }
        catch (Exception primaryError)
        {
            try { await StopAndRemoveAsync(created.Id, CancellationToken.None); }
            catch (Exception cleanupError) { throw new AggregateException("Docker application start failed and cleanup also failed.", primaryError, cleanupError); }
            throw;
        }
    }

    public async Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerIdOrName)) return;
        var result = await ExecuteAsync(["rm", "-f", containerIdOrName], TimeSpan.FromSeconds(30), cancellationToken, allowFailure: true);
        if (result.ExitCode != 0 && !LooksAbsent(result.CombinedOutput))
            throw new InvalidOperationException($"Failed to remove managed container '{containerIdOrName}': {result.CombinedOutput.Trim()}");
    }

    public async Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            ["inspect", "--format", "{{.State.Running}}", containerIdOrName],
            TimeSpan.FromSeconds(15), cancellationToken, allowFailure: true);
        if (result.ExitCode != 0)
        {
            if (LooksAbsent(result.CombinedOutput))
                return false;
            throw new InvalidOperationException($"Docker inspect failed for managed container '{containerIdOrName}': {result.CombinedOutput.Trim()}");
        }
        return ParseRunningState(result.StandardOutput, containerIdOrName);
    }

    public async Task<DockerContainerIdentity> InspectAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            ["inspect", "--format", "{{.Id}}|{{.State.Running}}|{{json .Config.Labels}}", containerIdOrName],
            TimeSpan.FromSeconds(15), cancellationToken, allowFailure: true);
        if (result.ExitCode != 0)
        {
            if (LooksAbsent(result.CombinedOutput))
                return new DockerContainerIdentity(containerIdOrName, false, false, new Dictionary<string, string>());
            throw new InvalidOperationException($"Docker inspect failed for managed container '{containerIdOrName}': {result.CombinedOutput.Trim()}");
        }
        var parts = result.StandardOutput.Trim().Split('|', 3);
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]))
            throw MalformedInspection(containerIdOrName, result.StandardOutput);
        var running = ParseRunningState(parts[1], containerIdOrName);
        Dictionary<string, string> labels;
        try
        {
            labels = JsonSerializer.Deserialize<Dictionary<string, string>>(parts[2])
                ?? throw new JsonException("Docker returned null labels.");
        }
        catch (JsonException)
        {
            throw MalformedInspection(containerIdOrName, result.StandardOutput);
        }
        if (labels.Keys.Any(string.IsNullOrWhiteSpace))
        {
                throw MalformedInspection(containerIdOrName, result.StandardOutput);
        }
        return new DockerContainerIdentity(
            parts[0].Trim(),
            true,
            running,
            labels);
    }

    public async Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => (await ExecuteAsync(["logs", "--tail", "200", containerIdOrName], TimeSpan.FromSeconds(30), cancellationToken, allowFailure: true)).CombinedOutput;

    public async Task RemoveTargetContainersAsync(ApplicationContainerScope scope, CancellationToken cancellationToken = default)
    {
        var listed = await ExecuteAsync(["ps", "--all", "--quiet", "--no-trunc",
            "--filter", "label=com.automaton.managed=true",
            "--filter", "label=com.automaton.runtime-kind=generic",
            "--filter", $"label=com.automaton.app-id={scope.AppId}",
            "--filter", $"label=com.automaton.deployment-target-id={scope.DeploymentTargetId}"],
            TimeSpan.FromSeconds(30), cancellationToken);
        foreach (var id in listed.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!HostContainerRuntimeValidation.IsContainerId(id))
                throw new InvalidOperationException("Docker returned an invalid container identity.");
            var identity = await InspectAsync(id, cancellationToken);
            if (!identity.Exists) continue;
            if (!RunnerRuntimeMetadataPolicy.HasExpectedIdentity(identity, scope.AppId, scope.DeploymentTargetId))
                throw new UnauthorizedAccessException("Runtime cleanup ownership does not match.");
            await StopAndRemoveAsync(id, cancellationToken);
        }
    }

    private async Task<CommandResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        if (commandExecutor is not null)
        {
            var result = await commandExecutor(arguments, timeout, cancellationToken, allowFailure);
            if (!allowFailure && result.ExitCode != 0)
                throw new InvalidOperationException($"Docker command failed ({DescribeArguments(arguments)}): {result.CombinedOutput.Trim()}");
            return result;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = dockerExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Docker CLI.");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var result = new CommandResult(process.ExitCode, stdout, stderr);
            if (!allowFailure && result.ExitCode != 0)
                throw new InvalidOperationException($"Docker command failed ({DescribeArguments(arguments)}): {result.CombinedOutput.Trim()}");
            return result;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    internal static void AddEnvironment(List<string> args, IReadOnlyDictionary<string, string> environment)
    {
        foreach (var pair in environment)
        {
            if (pair.Key.Contains('=') || pair.Key.Contains('\0'))
                throw new InvalidOperationException("Container environment variable name is invalid.");
            args.Add("--env");
            args.Add($"{pair.Key}={pair.Value}");
        }
    }

    internal static void AddEntrypoint(List<string> args, ApplicationCommandSpec command, string root)
    {
        if (command.UseShell)
        {
            args.Add("--entrypoint");
            args.Add("/bin/sh");
            return;
        }

        args.Add("--entrypoint");
        args.Add(command.Executable.Contains('/')
            ? Path.Combine(root, command.Executable).Replace('\\', '/')
            : command.Executable);
    }

    internal static void AddCommandArguments(List<string> args, ApplicationCommandSpec command)
    {
        if (command.UseShell)
        {
            args.Add("-c");
            args.Add(string.Join(" ", new[] { command.Executable }.Concat(command.Arguments).Select(QuoteShellArgument)));
            return;
        }
        args.AddRange(command.Arguments);
    }

    internal static string ResolveContainerWorkingDirectory(string path, string root = "/automaton/source")
    {
        if (string.IsNullOrWhiteSpace(path) || path == ".") return root;
        if (Path.IsPathRooted(path)) throw new InvalidOperationException("Container working directory must be relative.");
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
            throw new InvalidOperationException("Container working directory escapes its managed root.");
        return $"{root}/{string.Join('/', parts)}";
    }

    private static string MapHostPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists("/.dockerenv"))
            return fullPath;

        var runnerRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var hostRoot = Environment.GetEnvironmentVariable("AUTOMATON_HOST_WORKSPACE_ROOT");
        if (string.IsNullOrWhiteSpace(runnerRoot) || string.IsNullOrWhiteSpace(hostRoot))
            throw new InvalidOperationException("Containerized Runner requires AUTOMATON_HOST_WORKSPACE_ROOT to bind managed workspace paths safely.");
        return new RunnerManagedHostPathTranslator(runnerRoot, hostRoot).Translate(fullPath);
    }

    private static string ResolveApplicationNetwork() => "bridge";

    private static string DescribeArguments(IReadOnlyList<string> arguments) =>
        string.Join(' ', arguments.Select((argument, index) =>
            index > 0 && string.Equals(arguments[index - 1], "--env", StringComparison.Ordinal)
                ? argument[..Math.Min(argument.IndexOf('=') < 0 ? argument.Length : argument.IndexOf('='), 80)] + "=<redacted>"
                : argument));

    private static bool LooksAbsent(string output) =>
        output.Contains("No such container", StringComparison.OrdinalIgnoreCase)
        || output.Contains("no such object", StringComparison.OrdinalIgnoreCase);

    private static bool ParseRunningState(string value, string containerIdOrName)
    {
        var normalized = value.Trim();
        if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        throw MalformedInspection(containerIdOrName, value);
    }

    private static InvalidOperationException MalformedInspection(string containerIdOrName, string output)
        => new($"Docker inspect returned malformed state for managed container '{containerIdOrName}': {output.Trim()}");

    private static void ValidateImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image) || image.Any(char.IsWhiteSpace) || image.Contains('\0'))
            throw new ArgumentException("OCI image reference is invalid.", nameof(image));
    }

    internal static string QuoteShellArgument(string value)
        => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    internal delegate Task<CommandResult> DockerCommandExecutor(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool allowFailure);

    internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + StandardError;
    }
}
