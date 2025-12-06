# Phase 1: Direct Step Execution API

## Overview

Enable Apps to execute Custom Steps directly without requiring a Flow wrapper. This is the foundation for simplified App+Backend workflows.

**Estimated Effort:** 2-3 days

---

## Goal

Add `automatonApi.executeStep()` capability so Apps can call Custom Steps directly:

```javascript
// NEW: Direct step execution
const result = await automatonApi.executeStep('SendEmailStep', {
    inputs: { to: 'user@example.com', subject: 'Hello', body: 'Message' }
});

// Access output variables
console.log(result.outputVariables.messageId);
```

---

## Components to Create/Modify

### 1. New Controller: `AppsStepExecutionController.cs`

**Location:** `src/Automaton.Studio.Server/Controllers/AppsStepExecutionController.cs`

```csharp
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for executing Custom Steps directly from published Web Apps
/// </summary>
[ApiController]
[Route("api/web-apps")]
public class AppsStepExecutionController : ControllerBase
{
    private readonly AppsService appsService;
    private readonly CustomStepsService customStepsService;
    private readonly AppsStepExecutionService stepExecutionService;
    private readonly Serilog.ILogger logger;

    public AppsStepExecutionController(
        AppsService appsService,
        CustomStepsService customStepsService,
        AppsStepExecutionService stepExecutionService)
    {
        this.appsService = appsService;
        this.customStepsService = customStepsService;
        this.stepExecutionService = stepExecutionService;
        this.logger = Serilog.Log.ForContext<AppsStepExecutionController>();
    }

    /// <summary>
    /// Execute a Custom Step from a published Web App
    /// </summary>
    /// <param name="appSlug">The app's public URL slug</param>
    /// <param name="stepIdentifier">Step name or GUID</param>
    /// <param name="request">Execution request with inputs and runner selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("{appSlug}/steps/{stepIdentifier}/execute")]
    [AllowAnonymous] // Auth is checked internally based on app settings
    public async Task<ActionResult<WebAppStepExecutionResponse>> ExecuteStep(
        string appSlug,
        string stepIdentifier,
        [FromBody] WebAppStepExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.Information(
                "Step execution request: App={AppSlug}, Step={StepIdentifier}", 
                appSlug, stepIdentifier);

            // 1. Validate app exists and is published
            var app = await appsService.GetByPublicUrlAsync(appSlug);
            
            if (app == null)
            {
                logger.Warning("App not found: {AppSlug}", appSlug);
                return NotFound(new { error = "App not found" });
            }

            if (!app.IsPublished)
            {
                logger.Warning("App not published: {AppSlug}", appSlug);
                return NotFound(new { error = "App not found or not published" });
            }

            // 2. Check authentication requirement
            if (app.RequiresAuthentication && !User.Identity?.IsAuthenticated == true)
            {
                logger.Warning("Authentication required for app: {AppSlug}", appSlug);
                return Unauthorized(new { error = "Authentication required" });
            }

            // 3. Execute the step
            var result = await stepExecutionService.ExecuteStepForAppAsync(
                app,
                stepIdentifier,
                request.Inputs ?? new Dictionary<string, string>(),
                request.RunnerNames,
                cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Warning(ex, "Unauthorized step execution: {StepIdentifier}", stepIdentifier);
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.Warning(ex, "Invalid step execution: {StepIdentifier}", stepIdentifier);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Step execution failed: App={AppSlug}, Step={StepIdentifier}", 
                appSlug, stepIdentifier);
            return StatusCode(500, new { error = "Internal server error during step execution" });
        }
    }

    /// <summary>
    /// List available steps for an app (for discovery/debugging)
    /// </summary>
    [HttpGet("{appSlug}/steps")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<AppStepInfo>>> ListSteps(string appSlug)
    {
        try
        {
            var app = await appsService.GetByPublicUrlAsync(appSlug);
            
            if (app == null || !app.IsPublished)
            {
                return NotFound(new { error = "App not found or not published" });
            }

            if (app.RequiresAuthentication && !User.Identity?.IsAuthenticated == true)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            var steps = await stepExecutionService.GetLinkedStepsAsync(app.Id);
            
            return Ok(steps);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to list steps for app: {AppSlug}", appSlug);
            return StatusCode(500, new { error = "Failed to list steps" });
        }
    }
}
```

---

### 2. New Service: `AppsStepExecutionService.cs`

**Location:** `src/Automaton.Studio.Server/Services/AppsStepExecutionService.cs`

```csharp
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for executing Custom Steps from Web Apps
/// </summary>
public class AppsStepExecutionService
{
    private readonly ApplicationDbContext dataContext;
    private readonly CustomStepsService customStepsService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly ILogger logger;

    public AppsStepExecutionService(
        ApplicationDbContext dataContext,
        CustomStepsService customStepsService,
        IHubContext<AutomatonHub> automatonHub)
    {
        this.dataContext = dataContext;
        this.customStepsService = customStepsService;
        this.automatonHub = automatonHub;
        this.logger = Log.ForContext<AppsStepExecutionService>();
    }

    /// <summary>
    /// Execute a step for a web app
    /// </summary>
    public async Task<WebAppStepExecutionResponse> ExecuteStepForAppAsync(
        AppModel app,
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
            var step = await ResolveStepAsync(stepIdentifier, app.OwnerId);
            
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
        AppModel app, 
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
        
        var app = await dataContext.Apps
            .Include(a => a.AppUsers)
            .FirstOrDefaultAsync(a => a.Id == appId);

        if (app == null)
        {
            return Enumerable.Empty<AppStepInfo>();
        }

        var ownerId = app.AppUsers.FirstOrDefault()?.UserId;
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
                Language = s.Definition // Will need to deserialize
            })
            .ToListAsync();

        return steps;
    }
}
```

---

### 3. New Models

**Location:** `src/Automaton.Studio.Server/Models/WebAppStepExecution.cs`

```csharp
namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request to execute a Custom Step from a Web App
/// </summary>
public class WebAppStepExecutionRequest
{
    /// <summary>
    /// Input variables as key-value pairs
    /// </summary>
    public Dictionary<string, string>? Inputs { get; set; }

    /// <summary>
    /// Optional list of runner names to execute on
    /// </summary>
    public List<string>? RunnerNames { get; set; }
}

/// <summary>
/// Response from Custom Step execution
/// </summary>
public class WebAppStepExecutionResponse
{
    public bool Success { get; set; }
    public Guid StepId { get; set; }
    public string? StepName { get; set; }
    public string Status { get; set; } = "Unknown";
    public int RunnerCount { get; set; }
    public DateTime ExecutedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? OutputVariables { get; set; }
    public List<string>? Logs { get; set; }
}

/// <summary>
/// Basic step info for listing
/// </summary>
public class AppStepInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
}
```

---

### 4. Update `automatonApi.js`

**Location:** `src/Automaton.Studio/wwwroot/automatonApi.js`

Add the `executeStep` method:

```javascript
/**
 * Execute a Custom Step by name or GUID
 * @param {string} stepIdentifier - Step name or GUID
 * @param {object} options - Execution options
 * @param {object} options.inputs - Input variables as key-value pairs
 * @param {string[]} options.runnerNames - Optional array of runner names
 * @returns {Promise<object>} Step execution result
 */
function executeStep(stepIdentifier, options = {}) {
    const appSlug = getAppSlug();
    if (!appSlug) {
        return Promise.reject(new Error('Could not determine app slug'));
    }
    
    const path = `/api/web-apps/${appSlug}/steps/${encodeURIComponent(stepIdentifier)}/execute`;
    const body = { 
        inputs: options.inputs || {}, 
        runnerNames: options.runnerNames || null 
    };
    
    return apiCall(path, 'POST', body).then(response => response.data || response);
}

// Update the exposed API object
window.automatonApi = { 
    executeFlow: executeFlow, 
    executeStep: executeStep,  // NEW
    customCall: customCall, 
    getAppSlug: getAppSlug 
};
```

---

### 5. Register Services in DI

**Location:** `src/Automaton.Studio.Server/Program.cs` or `Startup.cs`

```csharp
// Add in ConfigureServices
services.AddScoped<AppsStepExecutionService>();
```

---

### 6. Update `AppsService.cs`

Add method to get app by public URL:

```csharp
/// <summary>
/// Get app by public URL slug
/// </summary>
public async Task<AppModel?> GetByPublicUrlAsync(string publicUrl)
{
    var entity = await dataContext.Apps
        .Include(a => a.AppUsers)
        .FirstOrDefaultAsync(x => x.PublicUrl == publicUrl);

    if (entity == null) return null;

    var model = MapToModel(entity);
    
    // Add owner ID for execution context
    model.OwnerId = entity.AppUsers.FirstOrDefault()?.UserId ?? Guid.Empty;
    
    return model;
}
```

Add `OwnerId` property to `AppModel`:

```csharp
public class AppModel
{
    // ... existing properties ...
    
    /// <summary>
    /// Owner user ID (for execution context)
    /// </summary>
    public Guid OwnerId { get; set; }
}
```

---

## Testing Checklist

- [ ] Create a simple Custom Step (e.g., "EchoStep" that returns input)
- [ ] Create and publish a Web App
- [ ] Call `automatonApi.executeStep('EchoStep', { inputs: { message: 'Hello' } })`
- [ ] Verify response includes output variables
- [ ] Test with invalid step name ? should return 404
- [ ] Test with unpublished app ? should return 404
- [ ] Test with auth-required app without login ? should return 401
- [ ] Test with multiple runners
- [ ] Verify logs capture step execution output

---

## Next Steps

After Phase 1 is complete:
1. Proceed to [Phase 2: Backend Actions Tab](./02-phase2-backend-actions-tab.md)
2. Or run parallel with UI work while API stabilizes
