using Automaton.Core.Models;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Deploys backend scripts to runners and executes controller actions via HTTP tunnel.
/// </summary>
public class AppsControllerExecutionService
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly TunnelRequestRegistry tunnelRegistry;
    private readonly AppsTunnelConfig tunnelConfig;
    private readonly Serilog.ILogger logger;

    public AppsControllerExecutionService(
        ApplicationDbContext dataContext,
        AppFileStorageService fileStorageService,
        IHubContext<AutomatonHub> automatonHub,
        TunnelRequestRegistry tunnelRegistry,
        IOptions<AppsTunnelConfig> tunnelConfig)
    {
        this.dataContext = dataContext;
        this.fileStorageService = fileStorageService;
        this.automatonHub = automatonHub;
        this.tunnelRegistry = tunnelRegistry;
        this.tunnelConfig = tunnelConfig.Value;
        this.logger = Serilog.Log.ForContext<AppsControllerExecutionService>();
    }

    // ── Deploy ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight summary of what would be deployed for an app.
    /// Used by the frontend to show pre-deploy notifications (e.g. package installation warnings).
    /// </summary>
    public async Task<DeployInfoResponse> GetDeployInfoAsync(Guid appId, Guid ownerId)
    {
        var scripts = await fileStorageService.ListBackendFilesAsync(ownerId, appId);
        var hasRequirements = scripts.Any(s => s.ControllerName == "requirements" && s.Language == "Text");
        return new DeployInfoResponse
        {
            ScriptCount = scripts.Count,
            HasRequirements = hasRequirements
        };
    }

    /// <summary>
    /// Pushes all backend scripts from the server filesystem to the app's configured runner(s).
    /// Called by the "Deploy Backend" button and automatically on runner reconnect.
    /// </summary>
    public async Task<DeployBackendResponse> DeployBackendToRunnersAsync(
        Guid appId,
        Guid ownerId,
        List<string>? runnerNames,
        CancellationToken cancellationToken)
    {
        var scripts = await fileStorageService.ListBackendFilesAsync(ownerId, appId);
        var hasRequirements = scripts.Any(s => s.ControllerName == "requirements" && s.Language == "Text");

        if (scripts.Count == 0)
            return new DeployBackendResponse { Success = true, DeployedCount = 0, Message = "No backend scripts found" };

        var runners = await ResolveRunnersByOwnerAsync(ownerId, runnerNames, appId);

        if (!runners.Any())
            return new DeployBackendResponse { Success = false, TotalScripts = scripts.Count, HasRequirements = hasRequirements, Message = "No runners available for deployment. Please select a runner in the app settings." };

        var deployed = 0;
        var errors = new List<string>();
        var installLogs = new List<string>();

        foreach (var runner in runners)
        {
            foreach (var script in scripts)
            {
                try
                {
                    var payload = new
                    {
                        AppId = appId,
                        ControllerName = script.ControllerName,
                        Code = script.Code,
                        Language = script.Language
                    };

                    var result = await automatonHub.Clients.Client(runner.ConnectionId)
                        .InvokeAsync<AppControllerUploadResult>(AutomatonHubMethods.UploadControllerScript, payload, cancellationToken);

                    if (result.Success)
                    {
                        deployed++;
                        if (result.Logs?.Count > 0)
                            installLogs.AddRange(result.Logs.Select(l => $"[{runner.Name}] {l}"));
                    }
                    else
                        errors.Add($"Runner '{runner.Name}' rejected {script.ControllerName}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Runner '{runner.Name}', script '{script.ControllerName}': {ex.Message}");
                    logger.Warning(ex, "Failed to deploy {ControllerName} to runner {RunnerName}", script.ControllerName, runner.Name);
                }
            }
        }

        return new DeployBackendResponse
        {
            Success = !errors.Any(),
            DeployedCount = deployed,
            TotalScripts = scripts.Count,
            RunnerCount = runners.Count(),
            HasRequirements = hasRequirements,
            Message = errors.Any() ? string.Join("; ", errors) : $"Deployed {deployed} script(s) to {runners.Count()} runner(s)",
            InstallLogs = installLogs.Count > 0 ? installLogs : null
        };
    }

    /// <summary>
    /// Deploys only Python controller scripts (skips requirements.txt / system-requirements.txt)
    /// to a specific runner. Fast — no pip/apt involved. Used for auto-deploy on first execution.
    /// </summary>
    private async Task DeployScriptsToRunnerAsync(Guid appId, Guid ownerId, Runner runner, CancellationToken cancellationToken)
    {
        var scripts = await fileStorageService.ListBackendFilesAsync(ownerId, appId);
        var pythonScripts = scripts.Where(s => s.Language != "Text").ToList();

        foreach (var script in pythonScripts)
        {
            try
            {
                var payload = new
                {
                    AppId = appId,
                    ControllerName = script.ControllerName,
                    Code = script.Code,
                    Language = script.Language
                };

                await automatonHub.Clients.Client(runner.ConnectionId)
                    .InvokeAsync<AppControllerUploadResult>(AutomatonHubMethods.UploadControllerScript, payload, cancellationToken);

                logger.Debug("Auto-deployed {ControllerName} to runner {RunnerName}", script.ControllerName, runner.Name);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Auto-deploy failed for {ControllerName} on runner {RunnerName}", script.ControllerName, runner.Name);
            }
        }
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    public async Task<AppControllerActionResponse> ExecuteActionAsync(
        PublishedAppInfo app,
        string controllerName,
        string actionName,
        Dictionary<string, JsonElement> inputs,
        List<string>? runnerNames,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var logs = new List<string>();

        try
        {
            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Starting controller action execution for app '{app.Name}'");

            var runners = await ResolveRunnersAsync(app, runnerNames);

            if (!runners.Any())
                throw new InvalidOperationException("No runners available for execution");

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Using {runners.Count()} runner(s)");

            var results = await ExecuteViaTunnelAsync(app.Id, app.OwnerId ?? Guid.Empty, controllerName, actionName, inputs, runners, cancellationToken, logs);

            var success = results.All(r => r.Success);
            var firstSuccess = results.FirstOrDefault(r => r.Success);
            var errors = results.Where(r => !r.Success).Select(r => r.Error).ToList();

            if (!success && errors.Any(e => e != null))
            {
                foreach (var error in errors.Where(e => e != null))
                    logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Error: {error}");
            }

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Execution completed. Success: {success}");

            return new AppControllerActionResponse
            {
                Success = success,
                ControllerName = controllerName,
                ActionName = actionName,
                Status = success ? "Completed" : "Failed",
                RunnerCount = runners.Count(),
                ExecutedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                Data = firstSuccess?.Data,
                Logs = logs,
                Error = errors.Any() ? string.Join("; ", errors) : null
            };
        }
        catch (Exception ex)
        {
            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR: {ex.Message}");

            return new AppControllerActionResponse
            {
                Success = false,
                ControllerName = controllerName,
                ActionName = actionName,
                Status = "Failed",
                ExecutedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                Logs = logs,
                Error = ex.Message
            };
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<IEnumerable<Runner>> ResolveRunnersAsync(PublishedAppInfo app, List<string>? runnerNames)
    {
        var query = dataContext.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == app.OwnerId))
            .Where(r => r.ConnectionId != null);

        if (runnerNames != null && runnerNames.Any())
            query = query.Where(r => runnerNames.Contains(r.Name));
        else if (app.ExecutionRunnerId.HasValue)
            query = query.Where(r => r.Id == app.ExecutionRunnerId.Value);

        return await query.ToListAsync();
    }

    private async Task<IEnumerable<Runner>> ResolveRunnersByOwnerAsync(Guid ownerId, List<string>? runnerNames, Guid appId)
    {
        var query = dataContext.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == ownerId))
            .Where(r => r.ConnectionId != null);

        if (runnerNames != null && runnerNames.Any())
            query = query.Where(r => runnerNames.Contains(r.Name));
        else
            query = query.Where(r => r.Id == dataContext.WebApps
                .Where(a => a.Id == appId)
                .Select(a => a.ExecutionRunnerId)
                .FirstOrDefault());

        return await query.ToListAsync();
    }

    private async Task<List<RunnerAppControllerResult>> ExecuteViaTunnelAsync(
        Guid appId,
        Guid ownerId,
        string controllerName,
        string actionName,
        Dictionary<string, JsonElement> inputs,
        IEnumerable<Runner> runners,
        CancellationToken cancellationToken,
        List<string> logs)
    {
        var results = new List<RunnerAppControllerResult>();

        foreach (var runner in runners)
        {
            try
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Executing on runner: {runner.Name}");
                logger.Debug("Tunnel request: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName}",
                    appId, controllerName, actionName, runner.Name);

                var body = JsonSerializer.Serialize(new
                {
                    app_id = appId,
                    action = actionName,
                    inputs = inputs
                });

                var tunnelResponse = await SendTunnelRequestAsync(runner, controllerName, body, cancellationToken);

                // Auto-deploy on 404 "not deployed" — scripts missing from runner, push them and retry once
                if (tunnelResponse.StatusCode == 404)
                {
                    logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Controller not found on runner — auto-deploying scripts...");
                    logger.Information("Auto-deploying scripts for AppId={AppId} to runner {RunnerName} (scripts not found on runner)", appId, runner.Name);

                    await DeployScriptsToRunnerAsync(appId, ownerId, runner, cancellationToken);

                    tunnelResponse = await SendTunnelRequestAsync(runner, controllerName, body, cancellationToken);
                }

                if (tunnelResponse.StatusCode >= 200 && tunnelResponse.StatusCode < 300)
                {
                    var payload = JsonSerializer.Deserialize<TunnelControllerResponse>(
                        tunnelResponse.Body ?? "{}",
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (payload?.Success == true)
                        logger.Information("Tunnel success: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName}",
                            appId, controllerName, actionName, runner.Name);
                    else
                        logger.Warning("Controller returned failure: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName} Error={Error}",
                            appId, controllerName, actionName, runner.Name, payload?.Error);

                    results.Add(new RunnerAppControllerResult
                    {
                        RunnerId = runner.Id,
                        Success = payload?.Success ?? false,
                        Data = payload?.Data,
                        Error = payload?.Error
                    });
                }
                else
                {
                    var errorDetail = tunnelResponse.Error ?? $"HTTP {tunnelResponse.StatusCode}";
                    logger.Warning("Tunnel HTTP error: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName} StatusCode={StatusCode} Error={Error}",
                        appId, controllerName, actionName, runner.Name, tunnelResponse.StatusCode, errorDetail);

                    logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR on {runner.Name}: {errorDetail}");

                    results.Add(new RunnerAppControllerResult
                    {
                        RunnerId = runner.Id,
                        Success = false,
                        Error = errorDetail
                    });
                }
            }
            catch (Exception ex)
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR on {runner.Name}: {ex.Message}");
                logger.Error(ex, "Tunnel exception: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName}",
                    appId, controllerName, actionName, runner.Name);

                results.Add(new RunnerAppControllerResult
                {
                    RunnerId = runner.Id,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return results;
    }

    private async Task<TunnelResponse> SendTunnelRequestAsync(Runner runner, string controllerName, string body, CancellationToken cancellationToken)
    {
        var (requestId, responseTask) = tunnelRegistry.Register(
            timeout: TimeSpan.FromSeconds(tunnelConfig.TimeoutSeconds),
            ct: cancellationToken);

        var tunnelRequest = new TunnelRequest
        {
            RequestId = requestId,
            Method = "POST",
            Path = $"/api/controller/{controllerName}",
            Body = body
        };

        await automatonHub.Clients
            .Client(runner.ConnectionId)
            .SendAsync(AutomatonHubMethods.TunnelRequest, tunnelRequest, cancellationToken);

        return await responseTask;
    }
}
