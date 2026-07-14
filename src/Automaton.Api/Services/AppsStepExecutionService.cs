using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

/// <summary>
/// Service for executing Custom Steps from Web Apps
/// </summary>
public class AppsStepExecutionService
{
    private readonly ApplicationDbContext dataContext;
    private readonly UserImpersonationServiceFactory<CustomStepsService> customStepsServiceFactory;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly Serilog.ILogger logger;

    public AppsStepExecutionService(
        ApplicationDbContext dataContext,
        UserImpersonationServiceFactory<CustomStepsService> customStepsServiceFactory,
        IHubContext<AutomatonHub> automatonHub)
    {
        this.dataContext = dataContext;
        this.customStepsServiceFactory = customStepsServiceFactory;
        this.automatonHub = automatonHub;
        this.logger = Serilog.Log.ForContext<AppsStepExecutionService>();
    }

    /// <summary>
    /// Execute a step for a web app
    /// </summary>
    public async Task<AppStepExecutionResponse> ExecuteStepForAppAsync(
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

            // 1. Resolve step by name or GUID with decrypted sensitive variables
            var step = await ResolveStepAsync(stepIdentifier, app.OwnerId ?? Guid.Empty);
            
            if (step == null)
            {
                throw new InvalidOperationException($"Step '{stepIdentifier}' not found");
            }

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Resolved step: {step.Name} (ID: {step.Id})");

            // 2. Apply runtime input overrides (only for non-sensitive variables)
            ApplyRuntimeInputs(step, inputs);

            // 3. Get runners
            var runners = await ResolveRunnersAsync(app, runnerNames);
            
            if (!runners.Any())
            {
                throw new InvalidOperationException("No runners available for execution");
            }

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Using {runners.Count()} runner(s)");

            // 4. Execute on runners
            var results = await ExecuteOnRunnersAsync(step, runners, cancellationToken, logs);

            // 5. Aggregate results
            var success = results.All(r => r.Success);
            var outputVariables = AggregateOutputVariables(results);
            var errors = results.Where(r => !r.Success).Select(r => r.Error).ToList();

            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Execution completed. Success: {success}");

            return new AppStepExecutionResponse
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
            
            return new AppStepExecutionResponse
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
    /// Resolve step by name or GUID with decrypted sensitive variables
    /// </summary>
    private async Task<Models.CustomStep?> ResolveStepAsync(string identifier, Guid ownerId)
    {
        // Create an owner-scoped service using the impersonation factory
        var ownerScopedService = customStepsServiceFactory.CreateForUser(ownerId);

        // Try parsing as GUID first
        if (Guid.TryParse(identifier, out var stepId))
        {
            return await ownerScopedService.GetForExecutionAsync(stepId);
        }

        // Otherwise, search by name with decryption
        return await ownerScopedService.GetByNameForExecutionAsync(identifier);
    }

    /// <summary>
    /// Apply runtime input overrides from the Web App caller.
    /// 
    /// SECURITY: Sensitive variables (API keys, passwords, etc.) cannot be overridden
    /// at runtime - they always use the decrypted values from the step definition.
    /// Only non-sensitive variables can be overridden by the caller.
    /// </summary>
    private void ApplyRuntimeInputs(Models.CustomStep step, Dictionary<string, string> inputs)
    {
        if (step.Definition?.CodeInputVariables == null || inputs == null || inputs.Count == 0)
        {
            return;
        }

        foreach (var inputVar in step.Definition.CodeInputVariables)
        {
            // SECURITY: Never allow runtime override of sensitive variables
            // Sensitive values (API keys, etc.) must come from the step definition only
            if (inputVar.IsSensitive)
            {
                continue;
            }

            // For non-sensitive variables, apply the runtime override if provided
            if (inputs.TryGetValue(inputVar.Name, out var runtimeValue))
            {
                inputVar.Value = runtimeValue;
            }
        }
    }

    /// <summary>
    /// Resolve runners based on configuration
    /// </summary>
    private async Task<IEnumerable<Runner>> ResolveRunnersAsync(
        PublishedAppInfo app, 
        List<string>? runnerNames)
    {
        var thresholdUtc = DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold;
        var query = dataContext.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == app.OwnerId))
            .Where(r => r.Purpose != RunnerPurposes.Infrastructure)
            .Where(r => r.ConnectionId != null
                && r.LastHeartbeatAtUtc.HasValue
                && r.LastHeartbeatAtUtc.Value >= thresholdUtc);

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
    /// Execute step on multiple runners
    /// </summary>
    private async Task<List<RunnerCustomStepResult>> ExecuteOnRunnersAsync(
        Models.CustomStep step,
        IEnumerable<Runner> runners,
        CancellationToken cancellationToken,
        List<string> logs)
    {
        var results = new List<RunnerCustomStepResult>();

        foreach (var runner in runners)
        {
            try
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Executing on runner: {runner.Name}");

                if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
                {
                    throw new InvalidOperationException($"Runner '{runner.Name}' is not connected");
                }

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
    private Dictionary<string, string> AggregateOutputVariables(List<RunnerCustomStepResult> results)
    {
        var output = new Dictionary<string, string>();

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
        var stepEntities = await dataContext.WebAppSteps
            .Where(link => link.WebAppId == appId)
            .Select(link => link.CustomStep)
            .ToListAsync();

        var steps = stepEntities.Select(s => 
        {
            var definition = string.IsNullOrEmpty(s.Definition)
                ? new CustomStepDefinition()
                : JsonSerializer.Deserialize<CustomStepDefinition>(s.Definition) ?? new CustomStepDefinition();

            return new AppStepInfo
            {
                Id = s.Id,
                Name = s.Name,
                DisplayName = s.DisplayName,
                Description = s.Description,
                Language = definition.Language
            };
        }).ToList();

        return steps;
    }
}
