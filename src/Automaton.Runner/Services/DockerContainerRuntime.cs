using System.Diagnostics;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed record DockerImageIdentity(string RequestedReference, string ResolvedReference);

public sealed record DockerBuildRequest(
    string Image,
    ApplicationCommandSpec Command,
    string SourceRoot,
    int TimeoutSeconds);

public sealed record DockerApplicationContainerRequest(
    string Name,
    string Image,
    string ArtifactRoot,
    string WorkingDirectory,
    ApplicationCommandSpec Command,
    int Port,
    string DataDirectory,
    IReadOnlyDictionary<string, string> StorageMounts,
    Guid AppId,
    Guid DeploymentTargetId);

public sealed record DockerApplicationContainer(
    string Id,
    string Name,
    DockerImageIdentity Image);

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
    Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default);
}

public sealed class DockerContainerRuntime : IApplicationContainerRuntime
{
    private readonly string dockerExecutable;

    public DockerContainerRuntime(string dockerExecutable = "docker")
    {
        this.dockerExecutable = dockerExecutable;
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
        var args = new List<string>
        {
            "run", "--rm", "--network", "bridge",
            "--mount", $"type=bind,source={MapHostPath(request.SourceRoot)},target=/automaton/source",
            "--workdir", ResolveContainerWorkingDirectory(request.Command.WorkingDirectory),
            "--security-opt", "no-new-privileges",
            "--cap-drop", "ALL"
        };
        AddEnvironment(args, environment);
        args.Add(image.ResolvedReference);
        AddCommand(args, request.Command, "/automaton/source");
        await ExecuteAsync(args, TimeSpan.FromSeconds(request.TimeoutSeconds), cancellationToken);
    }

    public async Task<DockerApplicationContainer> StartApplicationAsync(
        DockerApplicationContainerRequest request,
        CancellationToken cancellationToken = default)
    {
        var image = await ResolveImageAsync(request.Image, cancellationToken);
        var network = ResolveApplicationNetwork(request.Port);
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
        if (network == "bridge")
        {
            args.Add("--publish");
            args.Add($"127.0.0.1:{request.Port}:{request.Port}");
        }

        foreach (var mount in request.StorageMounts)
        {
            args.Add("--mount");
            args.Add($"type=bind,source={MapHostPath(mount.Value)},target=/automaton/data/{mount.Key}");
        }

        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AUTOMATON_PORT"] = request.Port.ToString(),
            ["AUTOMATON_APP_DATA_DIR"] = "/automaton/data",
            ["AUTOMATON_APP_ID"] = request.AppId.ToString(),
            ["AUTOMATON_DEPLOYMENT_TARGET_ID"] = request.DeploymentTargetId.ToString()
        };
        foreach (var pair in request.Command.Environment)
            environment[pair.Key] = pair.Value;
        foreach (var mount in request.StorageMounts)
            environment[$"AUTOMATON_STORAGE_{mount.Key.ToUpperInvariant()}"] = $"/automaton/data/{mount.Key}";
        AddEnvironment(args, environment);

        args.Add(image.ResolvedReference);
        AddCommand(args, request.Command, "/automaton/app");
        var created = await ExecuteAsync(args, TimeSpan.FromSeconds(30), cancellationToken);
        var id = created.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Docker did not return an application container ID.");
        await ExecuteAsync(["start", id], TimeSpan.FromSeconds(30), cancellationToken);
        return new DockerApplicationContainer(id, request.Name, image);
    }

    public async Task StopAndRemoveAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerIdOrName)) return;
        await ExecuteAsync(["rm", "-f", containerIdOrName], TimeSpan.FromSeconds(30), cancellationToken, allowFailure: true);
    }

    public async Task<bool> IsRunningAsync(string containerIdOrName, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(
            ["inspect", "--format", "{{.State.Running}}", containerIdOrName],
            TimeSpan.FromSeconds(15), cancellationToken, allowFailure: true);
        return result.ExitCode == 0 && string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> GetLogsAsync(string containerIdOrName, CancellationToken cancellationToken = default)
        => (await ExecuteAsync(["logs", "--tail", "200", containerIdOrName], TimeSpan.FromSeconds(30), cancellationToken, allowFailure: true)).CombinedOutput;

    private async Task<CommandResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
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
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        var result = new CommandResult(process.ExitCode, stdout, stderr);
        if (!allowFailure && result.ExitCode != 0)
            throw new InvalidOperationException($"Docker command failed ({string.Join(' ', arguments)}): {result.CombinedOutput.Trim()}");
        return result;
    }

    private static void AddEnvironment(List<string> args, IReadOnlyDictionary<string, string> environment)
    {
        foreach (var pair in environment)
        {
            if (pair.Key.Contains('=') || pair.Key.Contains('\0'))
                throw new InvalidOperationException("Container environment variable name is invalid.");
            args.Add("--env");
            args.Add($"{pair.Key}={pair.Value}");
        }
    }

    private static void AddCommand(List<string> args, ApplicationCommandSpec command, string root)
    {
        if (command.UseShell)
        {
            args.Add("/bin/sh");
            args.Add("-c");
            args.Add(string.Join(" ", new[] { command.Executable }.Concat(command.Arguments).Select(QuoteShellArgument)));
            return;
        }

        args.Add(command.Executable.Contains('/')
            ? Path.Combine(root, command.Executable).Replace('\\', '/')
            : command.Executable);
        args.AddRange(command.Arguments);
    }

    private static string ResolveContainerWorkingDirectory(string path, string root = "/automaton/source")
        => path == "." ? root : $"{root}/{path.Replace('\\', '/').TrimStart('/')}";

    private static string MapHostPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists("/.dockerenv") && Directory.Exists("/host") && !fullPath.StartsWith("/host/", StringComparison.Ordinal))
            return "/host" + fullPath;
        return fullPath;
    }

    private static string ResolveApplicationNetwork(int port)
    {
        // Sharing the Runner network namespace keeps the existing loopback
        // gateway contract while still avoiding a public application port.
        if (File.Exists("/.dockerenv")
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOSTNAME")))
            return $"container:{Environment.GetEnvironmentVariable("HOSTNAME")}";
        return "bridge";
    }

    private static void ValidateImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image) || image.Any(char.IsWhiteSpace) || image.Contains('\0'))
            throw new ArgumentException("OCI image reference is invalid.", nameof(image));
    }

    private static string QuoteShellArgument(string value)
        => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + StandardError;
    }
}
