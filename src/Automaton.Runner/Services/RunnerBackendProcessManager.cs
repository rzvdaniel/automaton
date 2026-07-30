using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

public interface IAppBackendProcessManager
{
    Task EnsureRunningAsync(
        Guid appId,
        Guid deploymentTargetId,
        CancellationToken cancellationToken = default);
    Task StopAsync(Guid appId, Guid deploymentTargetId);
}

/// <summary>Owns one loopback backend process per deployed app.</summary>
public sealed class RunnerBackendProcessManager : IAppBackendProcessManager, IHostedService, IDisposable
{
    private readonly RunnerWorkspaceService workspace;
    private readonly RunnerBackendRegistry registry;
    private readonly ILogger<RunnerBackendProcessManager> logger;
    private readonly object gate = new();
    private readonly Dictionary<DeploymentRuntimeKey, Process> processes = [];
    private readonly int portStart = ReadPort("AUTOMATON_BACKEND_PORT_START", 18000);
    private readonly int portEnd = ReadPort("AUTOMATON_BACKEND_PORT_END", 18999);

    public RunnerBackendProcessManager(RunnerWorkspaceService workspace, RunnerBackendRegistry registry, ILogger<RunnerBackendProcessManager> logger)
    {
        this.workspace = workspace;
        this.registry = registry;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var metadata in Directory.Exists(workspace.AppsRoot)
            ? Directory.EnumerateFiles(workspace.AppsRoot, "deployment.json", SearchOption.AllDirectories)
            : [])
        {
            try
            {
                var deployment = JsonSerializer.Deserialize<BackendDeploymentMetadata>(await File.ReadAllTextAsync(metadata, cancellationToken));
                if (deployment is { HasBackend: true } &&
                    Guid.TryParse(deployment.AppId, out var appId) &&
                    Guid.TryParse(deployment.DeploymentTargetId, out var deploymentTargetId))
                {
                    await EnsureRunningAsync(
                        appId,
                        deploymentTargetId,
                        deployment.BackendModule,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not restore backend process from {MetadataPath}", metadata);
            }
        }
    }

    public Task EnsureRunningAsync(
        Guid appId,
        Guid deploymentTargetId,
        CancellationToken cancellationToken = default)
        => EnsureRunningAsync(appId, deploymentTargetId, "main:app", cancellationToken);

    private async Task EnsureRunningAsync(
        Guid appId,
        Guid deploymentTargetId,
        string? backendModule,
        CancellationToken cancellationToken)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        Process? old;
        lock (gate) processes.TryGetValue(key, out old);
        var port = AllocatePort();
        var backendRoot = workspace.GetAppBackendDirectory(appId, deploymentTargetId);
        var entrypoint = Path.Combine(backendRoot, "main.py");
        if (!File.Exists(entrypoint))
        {
            logger.LogWarning("Backend for app {AppId} has no main.py entrypoint", appId);
            return;
        }

        var logDir = Path.Combine(
            workspace.GetDeploymentTargetRoot(appId, deploymentTargetId),
            "logs");
        Directory.CreateDirectory(logDir);
        var log = new StreamWriter(Path.Combine(logDir, "backend.log"), append: true) { AutoFlush = true };
        var targetPython = RunnerBackendEnvironmentService.GetVenvPythonPath(
            workspace.GetAppVenvDirectory(appId, deploymentTargetId));
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("AUTOMATON_BACKEND_PYTHON")
                ?? (File.Exists(targetPython) ? targetPython : "python"),
            Arguments = $"-m uvicorn {SanitizeModule(backendModule)} --host 127.0.0.1 --port {port}",
            WorkingDirectory = backendRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["PORT"] = port.ToString();
        start.Environment["AUTOMATON_APP_ID"] = appId.ToString();
        start.Environment["AUTOMATON_DEPLOYMENT_TARGET_ID"] = deploymentTargetId.ToString();
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start backend for app {appId}.");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.Exited += (_, _) =>
        {
            var isCurrent = false;
            lock (gate)
            {
                isCurrent = processes.TryGetValue(key, out var current) && ReferenceEquals(current, process);
                if (isCurrent) processes.Remove(key);
            }
            if (isCurrent)
            {
                registry.Remove(appId, deploymentTargetId);
                logger.LogWarning(
                    "Backend process exited unexpectedly for app {AppId}, target {DeploymentTargetId}",
                    appId,
                    deploymentTargetId);
            }
            log.Dispose();
        };
        process.EnableRaisingEvents = true;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await WaitForPortAsync(port, cancellationToken);
            registry.Register(appId, deploymentTargetId, port);
            lock (gate) { processes[key] = process; }
            if (old is not null && !old.HasExited) old.Kill(entireProcessTree: true);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            log.Dispose();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Process[] active;
        lock (gate) active = processes.Values.ToArray();
        foreach (var pair in processes.Keys.ToArray())
            registry.Remove(pair.AppId, pair.DeploymentTargetId);
        foreach (var process in active)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
        }
        processes.Clear();
        return Task.CompletedTask;
    }

    public Task StopAsync(Guid appId, Guid deploymentTargetId)
    {
        var key = new DeploymentRuntimeKey(appId, deploymentTargetId);
        registry.Remove(appId, deploymentTargetId);
        lock (gate)
        {
            if (processes.Remove(key, out var process))
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                process.Dispose();
            }
        }
        return Task.CompletedTask;
    }

    private int AllocatePort()
    {
        lock (gate)
        {
            var used = processes.Values.Where(p => !p.HasExited).Select(p => p.StartInfo.EnvironmentVariables["PORT"]).ToHashSet();
            for (var port = portStart; port <= portEnd; port++)
                if (!used.Contains(port.ToString()) && CanBind(port)) return port;
        }
        throw new InvalidOperationException($"No backend ports available in range {portStart}-{portEnd}.");
    }

    private static bool CanBind(int port)
    {
        try { using var listener = new TcpListener(System.Net.IPAddress.Loopback, port); listener.Start(); return true; }
        catch (SocketException) { return false; }
    }

    private static async Task WaitForPortAsync(int port, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 100; i++)
        {
            try { using var client = new TcpClient(); await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cancellationToken); return; }
            catch (SocketException) { await Task.Delay(100, cancellationToken); }
        }
        throw new TimeoutException($"Backend did not listen on 127.0.0.1:{port}.");
    }

    private static int ReadPort(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var port) && port is > 0 and <= 65535 ? port : fallback;
    public void Dispose() => StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    private sealed record BackendDeploymentMetadata(
        string AppId,
        string DeploymentTargetId,
        bool HasBackend,
        string? BackendModule);

    private static string SanitizeModule(string? module)
    {
        var value = string.IsNullOrWhiteSpace(module) ? "main:app" : module.Trim();
        if (value.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or ':')))
            throw new InvalidOperationException("Backend module contains unsupported characters.");
        return value;
    }
}
