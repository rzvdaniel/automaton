using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for executing Custom Steps from Web Apps
/// </summary>
public class AppsStepExecutionService
{
    private readonly ApplicationDbContext dataContext;
    private readonly CustomStepsService customStepsService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly Serilog.ILogger logger;

    public AppsStepExecutionService(
        ApplicationDbContext dataContext,
        CustomStepsService customStepsService,
        IHubContext<AutomatonHub> automatonHub)
    {
        this.dataContext = dataContext;
        this.customStepsService = customStepsService;
        this.automatonHub = automatonHub;
        this.logger = Serilog.Log.ForContext<AppsStepExecutionService>();
    }

    /// <summary>
    /// Execute a step for a web app
    /// </summary>
    public async Task<WebAppStepExecutionResponse> ExecuteStepForAppAsync(
        PublishedAppInfo app,
        string stepIdentifier,
        Dictionary<string, string> inputs,
        List<string>? runnerNames,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var logs = new List<string>();

        try
        {
            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Starting step execution for app '{app.Name}'");

            // 1. Resolve step by name or GUID
            var step = await ResolveStepAsync(stepIdentifier, app.OwnerId ?? Guid.Empty);
            
            if (step == null)
            {
                throw new InvalidOperationException($"Step '{stepIdentifier}' not found");
            }

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Resolved step: {step.Name} (ID: {step.Id})");

            // 2. Check if step is linked to this app (optional security check)
            // For Phase 1, we allow any step owned by the app owner
            // Phase 2+ can add explicit linking requirement

            // 3. Get runners
            var runners = await ResolveRunnersAsync(app, runnerNames);
            
            if (!runners.Any())
            {
                throw new InvalidOperationException("No runners available for execution");
            }

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Using {runners.Count()} runner(s)");

            // 4. Prepare step with inputs
            var stepWithInputs = PrepareStepForExecution(step, inputs);

            // 5. Execute on runners
            var results = await ExecuteOnRunnersAsync(stepWithInputs, runners, cancellationToken, logs);

            // 6. Aggregate results
            var success = results.All(r => r.Success);
            var outputVariables = AggregateOutputVariables(results);
            var errors = results.Where(r => !r.Success).Select(r => r.Error).ToList();

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Execution completed. Success: {success}");

            return new WebAppStepExecutionResponse
            {
                Success = success,
                StepId = step.Id,
                StepName = step.Name,
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
            
            return new WebAppStepExecutionResponse
            {
                Success = false,
                Status = "Failed",
                ExecutedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                Logs = logs,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Resolve step by name or GUID
    /// </summary>
    private async Task<CustomStep?> ResolveStepAsync(string identifier, Guid ownerId)
    {
        // Try parsing as GUID first
        if (Guid.TryParse(identifier, out var stepId))
        {
            return await Task.FromResult(customStepsService.Get(stepId));
        }

        // Otherwise, search by name for steps owned by the app owner
        var steps = customStepsService.List(null);
        return steps.FirstOrDefault(s => 
            s.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolve runners based on configuration
    /// </summary>
    private async Task<IEnumerable<Entities.Runner>> ResolveRunnersAsync(
        PublishedAppInfo app, 
        List<string>? runnerNames)
    {
        var query = dataContext.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == app.OwnerId))
            .Where(r => r.ConnectionId != null); // Only online runners

        if (runnerNames != null && runnerNames.Any())
        {
            // Filter by specific runner names
            query = query.Where(r => runnerNames.Contains(r.Name));
        }
        else if (app.ExecutionRunnerId.HasValue)
        {
            // Use app's configured runner
            query = query.Where(r => r.Id == app.ExecutionRunnerId.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// Prepare step with input values
    /// </summary>
    private CustomStep PrepareStepForExecution(CustomStep step, Dictionary<string, string> inputs)
    {
        // Update input variable values
        if (step.Definition?.CodeInputVariables != null)
        {
            foreach (var inputVar in step.Definition.CodeInputVariables)
            {
                if (inputs.TryGetValue(inputVar.Name, out var value))
                {
                    inputVar.Value = value;
                }
            }
        }

        return step;
    }

    /// <summary>
    /// Execute step on multiple runners
    /// </summary>
    private async Task<List<RunnerCustomStepResult>> ExecuteOnRunnersAsync(
        CustomStep step,
        IEnumerable<Entities.Runner> runners,
        CancellationToken cancellationToken,
        List<string> logs)
    {
        var results = new List<RunnerCustomStepResult>();

        foreach (var runner in runners)
        {
            try
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Executing on runner: {runner.Name}");

                var client = automatonHub.Clients.Client(runner.ConnectionId);

                var payload = new
                {
                    Id = step.Id,
                    Name = step.Name,
                    Code = step.Definition.Code,
                    Language = step.Definition.Language,
                    CodeInputVariables = step.Definition.CodeInputVariables,
                    CodeOutputVariables = step.Definition.CodeOutputVariables
                };

                var response = await client.InvokeAsync<CustomStepExecutionResponse>(
                    AutomatonHubMethods.RunCustomStep,
                    payload,
                    cancellationToken);

                results.Add(new RunnerCustomStepResult
                {
                    CustomStepId = step.Id,
                    RunnerId = runner.Id,
                    Success = response.Success,
                    Output = response.Output,
                    Error = response.Error,
                    Started = response.Started,
                    Finished = response.Finished,
                    OutputVariables = response.OutputVariables
                });

                // Add step logs
                if (!string.IsNullOrEmpty(response.Output))
                {
                    foreach (var line in response.Output.Split('\n'))
                    {
                        logs.Add($"[{runner.Name}] {line}");
                    }
                }
            }
            catch (Exception ex)
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR on {runner.Name}: {ex.Message}");
                
                results.Add(new RunnerCustomStepResult
                {
                    CustomStepId = step.Id,
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

    /// <summary>
    /// Aggregate output variables from multiple runner results
    /// </summary>
    private Dictionary<string, object> AggregateOutputVariables(List<RunnerCustomStepResult> results)
    {
        var output = new Dictionary<string, object>();

        // Use first successful result's output variables
        var successResult = results.FirstOrDefault(r => r.Success && r.OutputVariables != null);
        if (successResult?.OutputVariables != null)
        {
            foreach (var kvp in successResult.OutputVariables)
            {
                output[kvp.Key] = kvp.Value;
            }
        }

        return output;
    }

    /// <summary>
    /// Get steps linked to an app
    /// </summary>
    public async Task<IEnumerable<AppStepInfo>> GetLinkedStepsAsync(Guid appId)
    {
        // For Phase 1, return all steps owned by app owner
        // Phase 2+ will use explicit AppCustomSteps linking
        
        var app = await dataContext.WebApps
            .Include(a => a.WebAppUsers)
            .FirstOrDefaultAsync(a => a.Id == appId);

        if (app == null)
        {
            return Enumerable.Empty<AppStepInfo>();
        }

        var ownerId = app.WebAppUsers.FirstOrDefault()?.UserId;
        if (ownerId == null)
        {
            return Enumerable.Empty<AppStepInfo>();
        }

        var steps = await dataContext.CustomSteps
            .Where(s => s.CustomStepUsers.Any(su => su.UserId == ownerId))
            .Select(s => new AppStepInfo
            {
                Id = s.Id,
                Name = s.Name,
                DisplayName = s.DisplayName,
                Description = s.Description,
                Language = s.Definition // Will need to extract language from JSON
            })
            .ToListAsync();

        return steps;
    }
}
