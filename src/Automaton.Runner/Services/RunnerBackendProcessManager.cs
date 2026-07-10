using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

public interface IAppBackendProcessManager
{
    Task EnsureRunningAsync(Guid appId, CancellationToken cancellationToken = default);
    Task StopAsync(Guid appId);
}

/// <summary>Owns one loopback backend process per deployed app.</summary>
public sealed class RunnerBackendProcessManager : IAppBackendProcessManager, IHostedService, IDisposable
{
    private readonly RunnerWorkspaceService workspace;
    private readonly RunnerBackendRegistry registry;
    private readonly ILogger<RunnerBackendProcessManager> logger;
    private readonly object gate = new();
    private readonly Dictionary<Guid, Process> processes = [];
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
                if (deployment is { HasBackend: true } && Guid.TryParse(deployment.AppId, out var appId))
                    await EnsureRunningAsync(appId, deployment.BackendModule, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not restore backend process from {MetadataPath}", metadata);
            }
        }
    }

    public Task EnsureRunningAsync(Guid appId, CancellationToken cancellationToken = default)
        => EnsureRunningAsync(appId, "main:app", cancellationToken);

    private async Task EnsureRunningAsync(Guid appId, string? backendModule, CancellationToken cancellationToken)
    {
        Process? old;
        lock (gate) processes.TryGetValue(appId, out old);
        var port = AllocatePort();
        var backendRoot = workspace.GetAppBackendDirectory(appId);
        var entrypoint = Path.Combine(backendRoot, "main.py");
        if (!File.Exists(entrypoint))
        {
            logger.LogWarning("Backend for app {AppId} has no main.py entrypoint", appId);
            return;
        }

        var logDir = Path.Combine(workspace.GetAppRoot(appId), "logs");
        Directory.CreateDirectory(logDir);
        var log = new StreamWriter(Path.Combine(logDir, "backend.log"), append: true) { AutoFlush = true };
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("AUTOMATON_BACKEND_PYTHON") ?? "python",
            Arguments = $"-m uvicorn {SanitizeModule(backendModule)} --host 127.0.0.1 --port {port}",
            WorkingDirectory = backendRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["PORT"] = port.ToString();
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start backend for app {appId}.");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.WriteLine(e.Data); };
        process.Exited += (_, _) =>
        {
            var isCurrent = false;
            lock (gate)
            {
                isCurrent = processes.TryGetValue(appId, out var current) && ReferenceEquals(current, process);
                if (isCurrent) processes.Remove(appId);
            }
            if (isCurrent)
            {
                registry.Remove(appId);
                logger.LogWarning("Backend process exited unexpectedly for app {AppId}", appId);
            }
            log.Dispose();
        };
        process.EnableRaisingEvents = true;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await WaitForPortAsync(port, cancellationToken);
            registry.Register(appId, port);
            lock (gate) { processes[appId] = process; }
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
        foreach (var pair in processes.Keys.ToArray()) registry.Remove(pair);
        foreach (var process in active)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
        }
        processes.Clear();
        return Task.CompletedTask;
    }

    public Task StopAsync(Guid appId)
    {
        registry.Remove(appId);
        lock (gate)
        {
            if (processes.Remove(appId, out var process))
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
    private sealed record BackendDeploymentMetadata(string AppId, bool HasBackend, string? BackendModule);

    private static string SanitizeModule(string? module)
    {
        var value = string.IsNullOrWhiteSpace(module) ? "main:app" : module.Trim();
        if (value.Any(character => !(char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or ':')))
            throw new InvalidOperationException("Backend module contains unsupported characters.");
        return value;
    }
}
