using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Handles deploying backend scripts to runners and executing controller actions.
/// Deploy phase: server reads files, pushes to runner via SignalR (stored on runner's persistent volume).
/// Execute phase: server sends only appId+controllerName+actionName+inputs — no code.
/// </summary>
public class AppsControllerExecutionService
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly Serilog.ILogger logger;

    public AppsControllerExecutionService(
        ApplicationDbContext dataContext,
        AppFileStorageService fileStorageService,
        IHubContext<AutomatonHub> automatonHub)
    {
        this.dataContext = dataContext;
        this.fileStorageService = fileStorageService;
        this.automatonHub = automatonHub;
        this.logger = Serilog.Log.ForContext<AppsControllerExecutionService>();
    }

    // ── Deploy ────────────────────────────────────────────────────────────────

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

        if (scripts.Count == 0)
            return new DeployBackendResponse { Success = true, DeployedCount = 0, Message = "No backend scripts found" };

        var runners = await ResolveRunnersByOwnerAsync(ownerId, runnerNames, appId);

        if (!runners.Any())
            return new DeployBackendResponse { Success = false, TotalScripts = scripts.Count, Message = "No runners available for deployment. Please select a runner in the app settings." };

        var deployed = 0;
        var errors = new List<string>();

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

                    var ok = await automatonHub.Clients.Client(runner.ConnectionId)
                        .InvokeAsync<bool>(AutomatonHubMethods.UploadControllerScript, payload, cancellationToken);

                    if (ok) deployed++;
                    else errors.Add($"Runner '{runner.Name}' rejected {script.ControllerName}");
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
            Message = errors.Any() ? string.Join("; ", errors) : $"Deployed {deployed} script(s) to {runners.Count()} runner(s)"
        };
    }

    /// <summary>
    /// Auto-sync: called when a specific runner reconnects to push all its app scripts.
    /// </summary>
    public async Task SyncScriptsToRunnerAsync(Guid runnerId, string connectionId, CancellationToken cancellationToken)
    {
        // Find all apps that have this runner configured and their owners (via WebAppUsers)
        var apps = await dataContext.WebApps
            .Where(a => a.ExecutionRunnerId == runnerId)
            .Join(dataContext.WebAppUsers,
                a => a.Id,
                wu => wu.WebAppId,
                (a, wu) => new { AppId = a.Id, OwnerId = wu.UserId })
            .ToListAsync(cancellationToken);

        foreach (var app in apps)
        {
            var scripts = await fileStorageService.ListBackendFilesAsync(app.OwnerId, app.AppId);
            foreach (var script in scripts)
            {
                try
                {
                    var payload = new
                    {
                        AppId = app.AppId,
                        ControllerName = script.ControllerName,
                        Code = script.Code,
                        Language = script.Language
                    };

                    await automatonHub.Clients.Client(connectionId)
                        .InvokeAsync<bool>(AutomatonHubMethods.UploadControllerScript, payload, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Auto-sync failed for {ControllerName} app {AppId} runner {RunnerId}",
                        script.ControllerName, app.AppId, runnerId);
                }
            }
        }
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    public async Task<AppControllerActionResponse> ExecuteActionAsync(
        PublishedAppInfo app,
        string controllerName,
        string actionName,
        Dictionary<string, string> inputs,
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

            var results = await ExecuteOnRunnersAsync(app.Id, controllerName, actionName, inputs, runners, cancellationToken, logs);

            var success = results.All(r => r.Success);
            var outputVariables = AggregateOutputVariables(results);
            var errors = results.Where(r => !r.Success).Select(r => r.Error).ToList();

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
                OutputVariables = outputVariables,
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

    private async Task<List<RunnerAppControllerResult>> ExecuteOnRunnersAsync(
        Guid appId,
        string controllerName,
        string actionName,
        Dictionary<string, string> inputs,
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

                // Lean payload — no code, runner reads from local disk
                // OutputVariableNames: the standard contract for app controller scripts —
                // every script must set success (bool), result (any), error_message (str).
                var payload = new
                {
                    AppId = appId,
                    ControllerName = controllerName,
                    ActionName = actionName,
                    Inputs = inputs,
                    OutputVariableNames = new List<string> { "success", "result", "error_message" }
                };

                var response = await automatonHub.Clients.Client(runner.ConnectionId)
                    .InvokeAsync<AppControllerExecutionResponse>(
                        AutomatonHubMethods.RunAppControllerAction,
                        payload,
                        cancellationToken);

                results.Add(new RunnerAppControllerResult
                {
                    RunnerId = runner.Id,
                    Success = response.Success,
                    Output = response.Output,
                    Error = response.Error,
                    Started = response.Started,
                    Finished = response.Finished,
                    OutputVariables = response.OutputVariables
                });

                if (!string.IsNullOrEmpty(response.Output))
                {
                    foreach (var line in response.Output.Split('\n'))
                        logs.Add($"[{runner.Name}] {line}");
                }
            }
            catch (Exception ex)
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR on {runner.Name}: {ex.Message}");

                results.Add(new RunnerAppControllerResult
                {
                    RunnerId = runner.Id,
                    Success = false,
                    Error = ex.Message,
                    Started = DateTime.UtcNow,
                    Finished = DateTime.UtcNow
                });
            }
        }

        return results;
    }

    private static Dictionary<string, string> AggregateOutputVariables(List<RunnerAppControllerResult> results)
    {
        var output = new Dictionary<string, string>();
        var successResult = results.FirstOrDefault(r => r.Success && r.OutputVariables != null);
        if (successResult?.OutputVariables != null)
        {
            foreach (var kvp in successResult.OutputVariables)
                output[kvp.Key] = kvp.Value;
        }
        return output;
    }
}
