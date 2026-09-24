using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class RunnerRuntimeProcessExecutionPolicy
{
    private const string RuntimePortEnvironmentVariable = "AUTOMATON_PORT";
    public static ProcessStartInfo CreateStartInfo(
        string executable,
        ApplicationCommandSpec command,
        string workingDirectory,
        int effectivePort,
        Guid appId,
        Guid deploymentTargetId,
        string targetRoot,
        RunnerApplicationStorageRuntime storage)
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
        startInfo.Environment["AUTOMATON_APP_DATA_DIR"] = storage.DataDirectory;
        foreach (var mount in storage.Mounts)
            startInfo.Environment[$"AUTOMATON_STORAGE_{mount.Key.ToUpperInvariant()}"] = mount.Value;
        return startInfo;
    }

    public static async Task WaitForReadinessAsync(
        Process runtime,
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
            EnsureRuntimeHasNotExited(runtime);
            if (await IsRuntimeReadyAsync(healthPort, health.Path, httpClient, timeout.Token)) return;
            if (!await DelayBeforeRetryAsync(timeout.Token)) break;
        }

        throw new TimeoutException(
            $"Runtime did not become ready on 127.0.0.1:{healthPort} within {health.StartupTimeoutSeconds} seconds.");
    }

    private static void EnsureRuntimeHasNotExited(Process runtime)
    {
        if (runtime.HasExited)
            throw new InvalidOperationException(
                $"Runtime process exited before becoming ready (exit code {runtime.ExitCode}).");
    }

    private static async Task<bool> IsRuntimeReadyAsync(
        int port,
        string? healthPath,
        HttpClient? httpClient,
        CancellationToken timeoutToken)
    {
        try
        {
            if (healthPath is null)
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port, timeoutToken);
                return true;
            }

            using var response = await httpClient!.GetAsync(
                new UriBuilder("http", IPAddress.Loopback.ToString(), port) { Path = healthPath }.Uri,
                timeoutToken);
            return (int)response.StatusCode is >= 200 and < 300;
        }
        catch (SocketException) when (!timeoutToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException) when (!timeoutToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TaskCanceledException) when (!timeoutToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<bool> DelayBeforeRetryAsync(CancellationToken timeoutToken)
    {
        try
        {
            await Task.Delay(100, timeoutToken);
            return true;
        }
        catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public static string ResolveExecutable(string root, string executable)
    {
        if (IsBareExecutable(executable))
            return executable;
        return ResolveWorkspacePath(root, executable, "executable");
    }

    public static string ResolveWorkspacePath(string root, string relativePath, string description)
        => ManagedPathBoundary.ResolveInside(
            root,
            relativePath,
            $"Runtime {description}");

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

    public static void WriteLine(StreamWriter writer, string? value)
    {
        if (value is null)
            return;

        try { writer.WriteLine(value); }
        catch (ObjectDisposedException) { }
    }

    public static StreamWriter CreateLogWriter(string path)
        => new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };

    public static async Task StopProcessAsync(Process process, CancellationToken cancellationToken = default)
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
            catch (Exception cleanupException) when (cleanupException is InvalidOperationException or TimeoutException or COMException)
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

    public static DateTimeOffset? GetProcessStartTime(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch { return null; }
    }

}
