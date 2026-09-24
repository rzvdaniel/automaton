using Automaton.Runner.Console.Services;
using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerLocalFastApiProcess
{
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly ILogger logger;

    public RunnerLocalFastApiProcess(RunnerWorkspaceService runnerWorkspaceService, ILogger logger)
    {
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.logger = logger;
    }
    private static bool IsRunningInContainer =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
        || File.Exists("/.dockerenv");

    /// <summary>
    /// Starts the local FastAPI backend process on non-container environments.
    /// FastAPI inherits the runner process environment — AUTOMATON_SERVER_URL, AUTOMATON_API_KEY,
    /// and RUNNER_SCRIPTS_DIR are standard env vars set by the operator.
    /// In Docker, this is a no-op — the entrypoint script already starts FastAPI.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunningInContainer)
        {
            logger.Debug("Running in container — FastAPI managed by entrypoint, skipping launch");
            return;
        }

        // Ask any running FastAPI instance (from a previous runner run) to shut down,
        // then wait until the port is actually free before launching the new one.
        // This is reliable across process restarts — no PID file or static field needed.
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await probe.GetAsync("http://localhost:8001/shutdown", cancellationToken);

            // Poll until the port is released (connection refused = old process exited)
            var stopDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < stopDeadline)
            {
                try
                {
                    using var check = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                    using var response = await check.GetAsync("http://localhost:8001/health", cancellationToken);
                    await Task.Delay(200, cancellationToken); // still bound — keep waiting
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    break; // connection refused — port is free
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch { /* not running — fine */ }

        var mainPy = Path.Combine(AppContext.BaseDirectory, "fastapi_server", "main.py");
        if (!File.Exists(mainPy))
        {
            logger.Warning("FastAPI main.py not found at {Path} — skipping auto-launch", mainPy);
            WriteLine($"[!] FastAPI main.py not found at {mainPy} — skipping auto-launch.");
            return;
        }

        // FastAPI inherits the runner's process environment but RUNNER_SCRIPTS_DIR may not be
        // set when running locally (dotnet run). Always inject it explicitly so the fallback
        // /app/data/apps (Docker-only) is never used on a standalone machine.
        var scriptsDir = runnerWorkspaceService.GetFastApiScriptsRoot();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{mainPy}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(mainPy)!
        };
        psi.Environment["RUNNER_SCRIPTS_DIR"] = scriptsDir;

        if (runnerWorkspaceService.UsesLegacyScriptsLayout)
        {
            psi.Environment.Remove("AUTOMATON_WORKSPACE_ROOT");
        }
        else
        {
            psi.Environment["AUTOMATON_WORKSPACE_ROOT"] = runnerWorkspaceService.WorkspaceRoot;
        }

        try
        {
            if (runnerWorkspaceService.UsesLegacyScriptsLayout)
            {
                logger.Debug(
                    "Launching FastAPI backend with RUNNER_SCRIPTS_DIR={ScriptsDir} in legacy scripts layout mode",
                    scriptsDir);
            }
            else
            {
                logger.Debug(
                    "Launching FastAPI backend with RUNNER_SCRIPTS_DIR={ScriptsDir} and workspace root {WorkspaceRoot}",
                    scriptsDir,
                    runnerWorkspaceService.WorkspaceRoot);
            }
            WriteLine($"🔧 FastAPI scripts dir: {scriptsDir}");
            var proc = System.Diagnostics.Process.Start(psi);
            logger.Debug("FastAPI backend launched (PID {Pid})", proc?.Id);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to auto-launch FastAPI backend — start it manually");
            WriteLine($"[!] Failed to auto-launch FastAPI backend: {ex.Message}");
            WriteLine("[!] Start it manually: python fastapi_server/main.py");
        }
    }

    /// <summary>
    /// Waits for the local FastAPI backend to become ready before connecting to the server hub.
    /// In Docker, FastAPI starts alongside the .NET runner; this health check ensures it's up.
    /// On bare-metal (no FastAPI), this times out gracefully and logs a warning.
    /// </summary>
    internal async Task WaitAsync(int maxWaitSeconds = 30, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync("http://localhost:8001/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    if (attempt > 0) WriteLine(); // newline after the dots
                    logger.Debug("FastAPI backend ready");
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch { /* not ready yet */ }

            if (attempt == 0)
                Write("⏳ Waiting for FastAPI backend");
            else
                Write(".");

            attempt++;
            await Task.Delay(1000, cancellationToken);
        }

        WriteLine(); // newline after the dots
        logger.Warning("FastAPI backend did not become ready within {Seconds}s - tunnel will be unavailable", maxWaitSeconds);
        WriteLine($"[!] FastAPI backend not ready after {maxWaitSeconds}s. App controller execution will not work.");
        WriteLine("[!] Run: pip install fastapi \"uvicorn[standard]\" and restart.");
    }
}
