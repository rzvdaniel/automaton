using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Automaton.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

public interface IAppRuntimeProcessManager
{
    Task ApplyAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationContract contract,
        CancellationToken cancellationToken = default);

    Task StopAsync(Guid appId, Guid deploymentTargetId);
}

/// <summary>
/// Starts and supervises the opaque runtime command described by an application
/// contract. This class intentionally has no knowledge of a language, framework,
/// package manager, or application source layout.
/// </summary>
public sealed class RunnerRuntimeProcessManager : IAppRuntimeProcessManager, IHostedService, IDisposable
{
    private const string MetadataFileName = "deployment.json";
    private const string RuntimePortEnvironmentVariable = "AUTOMATON_PORT";
    private readonly RunnerWorkspaceService workspace;
    private readonly RunnerRuntimeRegistry registry;
    private readonly ILogger<RunnerRuntimeProcessManager> logger;
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, ActiveRuntime> runtimes = [];
    private readonly Dictionary<DeploymentRuntimeKey, ActiveRuntime> candidates = [];
    private readonly int portStart = ReadPort("AUTOMATON_RUNTIME_PORT_START", 18000);
    private readonly int portEnd = ReadPort("AUTOMATON_RUNTIME_PORT_END", 18999);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public RunnerRuntimeProcessManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry,
        ILogger<RunnerRuntimeProcessManager> logger)
    {
        this.workspace = workspace;
        this.registry = registry;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var metadataPath in Directory.Exists(workspace.AppsRoot)
            ? Directory.EnumerateFiles(workspace.AppsRoot, MetadataFileName, SearchOption.AllDirectories)
            : [])
        {
            try
            {
                var metadata = await ReadMetadataAsync(metadataPath, cancellationToken);
                if (metadata is null || metadata.Runtime?.Command is null)
                    continue;

                if (!Guid.TryParse(metadata.AppId, out var appId)
                    || !Guid.TryParse(metadata.DeploymentTargetId, out var targetId))
                {
                    throw new InvalidOperationException("Runtime deployment metadata contains invalid identifiers.");
                }

                await StopRecordedProcessAsync(metadata);
                var candidate = await StartRuntimeAsync(
                    appId,
                    targetId,
                    metadata.Runtime,
                    metadata.Health ?? new ApplicationHealthSpec(),
                    metadata,
                    cancellationToken);
                await PromoteRuntimeAsync(
                    appId,
                    targetId,
                    metadata.Runtime,
                    metadata.Health ?? new ApplicationHealthSpec(),
                    candidate,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not restore runtime process from {MetadataPath}", metadataPath);
            }
        }
    }

    public async Task ApplyAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationContract contract,
        CancellationToken cancellationToken = default)
    {
        ApplicationContractValidator.EnsureValid(contract);
        var metadataPath = GetMetadataPath(appId, deploymentTargetId);
        var previousMetadata = File.Exists(metadataPath)
            ? await ReadMetadataAsync(metadataPath, cancellationToken)
            : null;

        if (contract.Runtime.Command is null)
        {
            await StopAsync(appId, deploymentTargetId);
            await WriteMetadataAsync(
                new RunnerRuntimeDeploymentMetadata
                {
                    AppId = appId.ToString(),
                    DeploymentTargetId = deploymentTargetId.ToString(),
                    Runtime = contract.Runtime,
                    Health = contract.Health,
                    State = RuntimeReadinessState.Stopped
                },
                cancellationToken);
            return;
        }

        var pending = new RunnerRuntimeDeploymentMetadata
        {
            AppId = appId.ToString(),
            DeploymentTargetId = deploymentTargetId.ToString(),
            Runtime = contract.Runtime,
            Health = contract.Health,
            State = RuntimeReadinessState.Starting
        };
        await WriteMetadataAsync(pending, cancellationToken);

        try
        {
            var candidate = await StartRuntimeAsync(
                appId,
                deploymentTargetId,
                contract.Runtime,
                contract.Health,
                previousMetadata,
                cancellationToken);
            await PromoteRuntimeAsync(
                appId,
                deploymentTargetId,
                contract.Runtime,
                contract.Health,
                candidate,
                cancellationToken);
        }
        catch
        {
            if (previousMetadata is not null)
                await WriteMetadataAsync(previousMetadata, CancellationToken.None);
            else if (File.Exists(metadataPath))
                File.Delete(metadataPath);
            throw;
        }
    }

    private async Task<ActiveRuntime> StartRuntimeAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationRuntimeSpec runtime,
        ApplicationHealthSpec health,
        RunnerRuntimeDeploymentMetadata? previousMetadata,
        CancellationToken cancellationToken)
    {
        var command = runtime.Command
            ?? throw new InvalidOperationException("A runtime command is required to start a runtime process.");
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        var requestedPort = previousMetadata?.EffectivePort ?? runtime.Port;
        var effectivePort = AllocatePort(requestedPort);
        var targetRoot = workspace.GetAppRuntimeApplicationDirectory(appId, deploymentTargetId);
        Directory.CreateDirectory(targetRoot);
        var workingDirectory = ResolveWorkspacePath(targetRoot, command.WorkingDirectory, "working directory");
        var executable = ResolveExecutable(targetRoot, command.Executable);
        var logDirectory = Path.Combine(targetRoot, "logs");
        Directory.CreateDirectory(logDirectory);
        var stdout = CreateLogWriter(Path.Combine(logDirectory, "runtime.stdout.log"));
        var stderr = CreateLogWriter(Path.Combine(logDirectory, "runtime.stderr.log"));
        Process? process = null;
        ActiveRuntime? active = null;

        try
        {
            var startInfo = CreateStartInfo(
                executable,
                command,
                workingDirectory,
                effectivePort,
                appId,
                deploymentTargetId,
                targetRoot);
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start runtime process for app {appId}.");
            process.OutputDataReceived += (_, args) => WriteLine(stdout, args.Data);
            process.ErrorDataReceived += (_, args) => WriteLine(stderr, args.Data);
            process.EnableRaisingEvents = true;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var runtimeProcess = new ActiveRuntime(process, effectivePort, stdout, stderr);
            active = runtimeProcess;
            process.Exited += (_, _) => OnProcessExited(key, runtimeProcess);
            lock (gate) candidates[key] = runtimeProcess;

            await WaitForReadinessAsync(process, effectivePort, health, cancellationToken);
            if (process.HasExited)
                throw new InvalidOperationException("Runtime process exited immediately after becoming ready.");
            return runtimeProcess;
        }
        catch
        {
            if (process is not null)
            {
                await StopProcessAsync(process);
                RemoveActiveRuntime(key, process);
            }
            if (active is null)
            {
                stdout.Dispose();
                stderr.Dispose();
            }
            throw;
        }
    }

    private async Task PromoteRuntimeAsync(
        Guid appId,
        Guid deploymentTargetId,
        ApplicationRuntimeSpec runtime,
        ApplicationHealthSpec health,
        ActiveRuntime candidate,
        CancellationToken cancellationToken)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        try
        {
            await WriteMetadataAsync(
                new RunnerRuntimeDeploymentMetadata
                {
                    AppId = appId.ToString(),
                    DeploymentTargetId = deploymentTargetId.ToString(),
                    Runtime = runtime,
                    Health = health,
                    EffectivePort = candidate.Port,
                    ProcessId = candidate.Process.Id,
                    ProcessStartTimeUtc = GetProcessStartTime(candidate.Process),
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    State = RuntimeReadinessState.Ready
                },
                cancellationToken);

            ActiveRuntime? old;
            lock (gate)
            {
                old = runtimes.GetValueOrDefault(key);
                runtimes[key] = candidate;
                candidates.Remove(key);
                registry.Register(appId, deploymentTargetId, candidate.Port);
            }

            if (old is not null && !old.Process.HasExited)
                await StopProcessAsync(old.Process);
        }
        catch
        {
            if (!IsCurrentRuntime(key, candidate))
            {
                await StopProcessAsync(candidate.Process);
                RemoveActiveRuntime(key, candidate.Process);
            }
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ActiveRuntime[] active;
        DeploymentRuntimeKey[] keys;
        lock (gate)
        {
            active = runtimes.Values.ToArray();
            keys = runtimes.Keys.ToArray();
            active = active.Concat(candidates.Values).ToArray();
            runtimes.Clear();
            candidates.Clear();
        }

        foreach (var key in keys)
            registry.Remove(key.AppId, key.DeploymentTargetId);
        foreach (var runtime in active)
        {
            await StopProcessAsync(runtime.Process, cancellationToken);
            runtime.Dispose();
        }
    }

    public async Task StopAsync(Guid appId, Guid deploymentTargetId)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        registry.Remove(appId, deploymentTargetId);
        ActiveRuntime[] active;
        lock (gate)
        {
            var selected = new List<ActiveRuntime>();
            if (runtimes.Remove(key, out var current))
                selected.Add(current);
            if (candidates.Remove(key, out var candidate)
                && !selected.Contains(candidate))
            {
                selected.Add(candidate);
            }

            active = selected.ToArray();
        }

        foreach (var runtime in active)
        {
            await StopProcessAsync(runtime.Process);
            runtime.Dispose();
        }
    }

    private void OnProcessExited(DeploymentRuntimeKey key, ActiveRuntime active)
    {
        var isCurrent = false;
        lock (gate)
        {
            isCurrent = runtimes.TryGetValue(key, out var current) && ReferenceEquals(current, active);
            if (isCurrent) runtimes.Remove(key);
            candidates.Remove(key, out var candidate);
            if (!isCurrent && candidate is not null && !ReferenceEquals(candidate, active))
                candidate.Dispose();
        }

        if (isCurrent)
        {
            registry.Remove(key.AppId, key.DeploymentTargetId);
            logger.LogWarning(
                "Runtime process exited unexpectedly for app {AppId}, target {DeploymentTargetId}",
                key.AppId,
                key.DeploymentTargetId);
            _ = MarkStoppedAsync(key, active.Process.ExitCode);
        }

        active.Dispose();
    }

    private async Task MarkStoppedAsync(DeploymentRuntimeKey key, int exitCode)
    {
        try
        {
            var path = GetMetadataPath(key.AppId, key.DeploymentTargetId);
            var metadata = await ReadMetadataAsync(path, CancellationToken.None);
            if (metadata is not null)
            {
                metadata.EffectivePort = null;
                metadata.ProcessId = null;
                metadata.ProcessStartTimeUtc = null;
                metadata.State = RuntimeReadinessState.Failed;
                metadata.LastError = $"Runtime process exited with code {exitCode}.";
                await WriteMetadataAsync(metadata, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not persist runtime exit state for app {AppId}", key.AppId);
        }
    }

    private async Task StopRecordedProcessAsync(RunnerRuntimeDeploymentMetadata metadata)
    {
        if (metadata.ProcessId is not int processId || processId <= 0)
            return;

        try
        {
            using var process = Process.GetProcessById(processId);
            if (metadata.ProcessStartTimeUtc is DateTimeOffset expected)
            {
                var actual = process.StartTime.ToUniversalTime();
                if (Math.Abs((actual - expected.UtcDateTime).TotalSeconds) > 2)
                    return;
            }

            await StopProcessAsync(process);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private bool IsCurrentRuntime(DeploymentRuntimeKey key, ActiveRuntime runtime)
    {
        lock (gate)
            return runtimes.TryGetValue(key, out var current) && ReferenceEquals(current, runtime);
    }

    private void RemoveActiveRuntime(DeploymentRuntimeKey key, Process process)
    {
        lock (gate)
        {
            if (runtimes.TryGetValue(key, out var current) && ReferenceEquals(current.Process, process))
                runtimes.Remove(key);
            if (candidates.TryGetValue(key, out var candidate) && ReferenceEquals(candidate.Process, process))
                candidates.Remove(key);
        }
    }

    private int AllocatePort(int requestedPort)
    {
        lock (gate)
        {
            var used = runtimes.Values.Concat(candidates.Values)
                .Where(runtime => !runtime.Process.HasExited)
                .Select(runtime => runtime.Port)
                .ToHashSet();

            if (!used.Contains(requestedPort) && CanBind(requestedPort))
                return requestedPort;

            for (var port = portStart; port <= portEnd; port++)
                if (!used.Contains(port) && CanBind(port))
                    return port;
        }

        throw new InvalidOperationException($"No runtime ports available in range {portStart}-{portEnd}.");
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        ApplicationCommandSpec command,
        string workingDirectory,
        int effectivePort,
        Guid appId,
        Guid deploymentTargetId,
        string targetRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.UseShell ? GetShellExecutable() : executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (command.UseShell)
        {
            var shellCommand = string.Join(
                " ",
                new[] { executable }.Concat(command.Arguments).Select(QuoteShellArgument));
            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/s");
                startInfo.ArgumentList.Add("/c");
            }
            else
            {
                startInfo.ArgumentList.Add("-c");
            }
            startInfo.ArgumentList.Add(shellCommand);
        }
        else
        {
            foreach (var argument in command.Arguments)
                startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in command.Environment)
            startInfo.Environment[pair.Key] = pair.Value;
        startInfo.Environment[RuntimePortEnvironmentVariable] = effectivePort.ToString();
        startInfo.Environment["AUTOMATON_APP_ID"] = appId.ToString();
        startInfo.Environment["AUTOMATON_DEPLOYMENT_TARGET_ID"] = deploymentTargetId.ToString();
        startInfo.Environment["AUTOMATON_RUNTIME_ROOT"] = targetRoot;
        return startInfo;
    }

    private static async Task WaitForReadinessAsync(
        Process process,
        int effectivePort,
        ApplicationHealthSpec health,
        CancellationToken cancellationToken)
    {
        var healthPort = health.Port ?? effectivePort;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(health.StartupTimeoutSeconds));
        using var httpClient = health.Path is null
            ? null
            : new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };

        while (!timeout.IsCancellationRequested)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"Runtime process exited before becoming ready (exit code {process.ExitCode}).");

            try
            {
                if (health.Path is null)
                {
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync(IPAddress.Loopback, healthPort, timeout.Token);
                    return;
                }
                else
                {
                    using var response = await httpClient!.GetAsync(
                        new UriBuilder("http", IPAddress.Loopback.ToString(), healthPort)
                        {
                            Path = health.Path
                        }.Uri,
                        timeout.Token);
                    if ((int)response.StatusCode is >= 200 and < 300)
                        return;
                }
            }
            catch (SocketException) when (!timeout.IsCancellationRequested)
            {
            }
            catch (HttpRequestException) when (!timeout.IsCancellationRequested)
            {
            }
            catch (TaskCanceledException) when (!timeout.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(100, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        throw new TimeoutException(
            $"Runtime did not become ready on 127.0.0.1:{healthPort} within {health.StartupTimeoutSeconds} seconds.");
    }

    private string GetMetadataPath(Guid appId, Guid deploymentTargetId)
        => Path.Combine(workspace.GetDeploymentTargetRoot(appId, deploymentTargetId), MetadataFileName);

    private async Task<RunnerRuntimeDeploymentMetadata?> ReadMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        var metadata = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            await File.ReadAllTextAsync(path, cancellationToken),
            JsonOptions);
        if (metadata?.Runtime is null)
            throw new InvalidOperationException("Runtime deployment metadata is invalid.");
        return metadata;
    }

    private async Task WriteMetadataAsync(
        RunnerRuntimeDeploymentMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path = GetMetadataPath(
            Guid.Parse(metadata.AppId),
            Guid.Parse(metadata.DeploymentTargetId));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(metadata, JsonOptions),
            cancellationToken);
    }

    private static string ResolveExecutable(string root, string executable)
    {
        if (IsBareExecutable(executable))
            return executable;
        return ResolveWorkspacePath(root, executable, "executable");
    }

    private static string ResolveWorkspacePath(string root, string relativePath, string description)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || (relativePath.Length >= 2 && char.IsLetter(relativePath[0]) && relativePath[1] == ':'))
            throw new InvalidOperationException($"Runtime {description} must be relative to the managed application workspace.");

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is ".."))
            throw new InvalidOperationException($"Runtime {description} must remain inside the managed application workspace.");

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Runtime {description} must remain inside the managed application workspace.");
        return fullPath;
    }

    private static bool IsBareExecutable(string value)
        => !string.IsNullOrWhiteSpace(value)
            && !value.Contains('/')
            && !value.Contains('\\')
            && !Path.IsPathRooted(value)
            && !(value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':');

    private static string GetShellExecutable() => OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";

    private static string QuoteShellArgument(string value)
        => OperatingSystem.IsWindows()
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : $"'{value.Replace("'", "'\\''")}'";

    private static void WriteLine(StreamWriter writer, string? value)
    {
        if (value is null)
            return;

        try { writer.WriteLine(value); }
        catch (ObjectDisposedException) { }
    }

    private static StreamWriter CreateLogWriter(string path)
        => new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };

    private static async Task StopProcessAsync(Process process, CancellationToken cancellationToken = default)
    {
        try
        {
            if (process.HasExited)
                return;

            if (cancellationToken.IsCancellationRequested)
            {
                await ForceKillProcessTreeAsync(process);
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                return;
            }

            process.CloseMainWindow();
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            try
            {
                await ForceKillProcessTreeAsync(process);
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (Exception cleanupException) when (cleanupException is InvalidOperationException or TimeoutException)
            {
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task ForceKillProcessTreeAsync(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            if (process.HasExited)
                return;

            using var taskKill = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                ArgumentList = { "/PID", process.Id.ToString(), "/T", "/F" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (taskKill is not null)
                await taskKill.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            return;
        }

        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
    }

    private static DateTimeOffset? GetProcessStartTime(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch { return null; }
    }

    private static bool CanBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int ReadPort(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var port)
            && port is > 0 and <= 65_535
            ? port
            : fallback;

    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();

    private sealed class ActiveRuntime(
        Process process,
        int port,
        StreamWriter stdout,
        StreamWriter stderr) : IDisposable
    {
        private int disposed;
        public Process Process { get; } = process;
        public int Port { get; } = port;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            stdout.Dispose();
            stderr.Dispose();
            Process.Dispose();
        }
    }
}

public static class RuntimeReadinessState
{
    public const string Starting = "starting";
    public const string Ready = "ready";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
}

public sealed class RunnerRuntimeDeploymentMetadata
{
    public string AppId { get; set; } = string.Empty;
    public string DeploymentTargetId { get; set; } = string.Empty;
    public ApplicationRuntimeSpec? Runtime { get; set; }
    public ApplicationHealthSpec? Health { get; set; }
    public int? EffectivePort { get; set; }
    public int? ProcessId { get; set; }
    public DateTimeOffset? ProcessStartTimeUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public string State { get; set; } = RuntimeReadinessState.Stopped;
    public string? LastError { get; set; }
}
