using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

public class CustomStepsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly RecentActivityService recentActivityService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly SensitiveVariableService sensitiveVariableService;
    private readonly Guid userId;
    private readonly Serilog.ILogger logger;
    private const string DefaultIcon = "user";
    private const string DefaultGroup = "My Steps";

    public CustomStepsService
    (
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService,
        IHubContext<AutomatonHub> automatonHub,
        ResourceAuthorizationService authorizationService,
        SensitiveVariableService sensitiveVariableService
    )
    {
        this.dataContext = dataContext;
        this.recentActivityService = recentActivityService;
        this.automatonHub = automatonHub;
        this.authorizationService = authorizationService;
        this.sensitiveVariableService = sensitiveVariableService;
        this.userId = userContextService.GetUserId();
        this.logger = Log.ForContext<CustomStepsService>();
    }

    public IEnumerable<CustomStep> List(Guid? projectId = null, bool? excludeAppLinked = null)
    {
        // Use authorization service to get custom steps filtered by project context
        var entities = authorizationService.GetCustomStepsQueryByContext(projectId);

        // Exclude steps that are linked to any WebApp (for Flow designer use)
        if (excludeAppLinked == true)
        {
            var appLinkedStepIds = dataContext.WebAppSteps.Select(was => was.CustomStepId).ToList();
            entities = entities.Where(x => !appLinkedStepIds.Contains(x.Id));
        }

        // Get WebApp associations for these steps
        var stepIds = entities.Select(x => x.Id).ToList();
        var webAppSteps = dataContext.WebAppSteps
            .Include(was => was.WebApp)
            .Where(was => stepIds.Contains(was.CustomStepId))
            .ToDictionary(was => was.CustomStepId, was => was.WebApp);

        var steps = entities.Select(entity => MapToModel(entity, webAppSteps));

        return steps;
    }

    public CustomStep Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var stepEntity = authorizationService.GetAccessibleCustomStepsQuery()
            .SingleOrDefault(x => x.Id == id);
        var step = MapToModel(stepEntity);

        // Return step with encrypted references as-is for UI
        // UI password inputs will handle visual hiding
        return step;
    }

    public async Task<Guid> Create(NewCustomStep step)
    { 
        var entity = new Entities.CustomStep
        {
            Id = Guid.NewGuid(),
            Name = step.Name,
            Icon = DefaultIcon,
            Category = DefaultGroup,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DisplayName = string.Empty,
            Description = string.Empty,
            MoreInfo = string.Empty,
            Definition = JsonSerializer.Serialize(new CustomStepDefinition())
        };

        dataContext.CustomSteps.Add(entity);

        var stepUser = new Entities.CustomStepUser
        {
            CustomStepId = entity.Id,
            UserId = userId
        };

        dataContext.CustomStepUsers.Add(stepUser);

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogCustomStepActivityAsync(ActivityType.CustomStepCreated, step.Name, entity.Id);

        return entity.Id;
    }

    public async Task Update(Guid id, CustomStep step)
    {
        // Check if user can update this custom step (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateCustomStepAsync(id))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update custom step {id}");
        }

        var entity = dataContext.CustomSteps.SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"Custom step {id} not found");
        }

        // ✅ Process sensitive variables in Definition before saving
        await ProcessSensitiveVariablesAsync(step.Definition, id);

        entity.Name = step.Name;
        entity.DisplayName = step.DisplayName;
        entity.Description = step.Description;
        entity.Icon = step.Icon;
        entity.Category = step.Category;
        entity.MoreInfo = step.MoreInfo;
        entity.VisibleInExplorer = step.VisibleInExplorer;
        entity.Updated = DateTime.UtcNow;
        entity.Definition = JsonSerializer.Serialize(step.Definition);

        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogCustomStepActivityAsync(ActivityType.CustomStepUpdated, step.Name, id);
    }

    public async Task Remove(Guid id)
    {
        // Check if user can delete this custom step (must be personal owner AND not part of any project)
        if (!await authorizationService.CanDeleteCustomStepAsync(id))
        {
            // Check if it's because the step is part of a project
            var isPartOfProject = await dataContext.ProjectCustomSteps
                .AnyAsync(pcs => pcs.CustomStepId == id);

            if (isPartOfProject)
            {
                throw new InvalidOperationException($"Cannot delete custom step {id} because it is part of one or more projects. Remove it from all projects first.");
            }

            throw new UnauthorizedAccessException($"You do not have permission to delete custom step {id}");
        }

        var customStep = dataContext.CustomSteps.SingleOrDefault(x => x.Id == id);

        if (customStep == null)
        {
            throw new InvalidOperationException($"Custom step {id} not found");
        }

        var stepName = customStep.Name;

        // ✅ Delete associated sensitive variables
        await DeleteSensitiveVariablesAsync(id);

        dataContext.CustomSteps.Remove(customStep);
        dataContext.SaveChanges();

        // Log activity
        await recentActivityService.LogCustomStepActivityAsync(ActivityType.CustomStepDeleted, stepName, id);
    }

    public bool Exists(string name)
    {
        var exists = dataContext.CustomSteps.Any(x => x.Name == name && x.CustomStepUsers.Any(x => x.UserId == userId));

        return exists;
    }

    /// <summary>
    /// Get a custom step by name (for API/MCP lookup)
    /// </summary>
    public CustomStep? GetByName(string name)
    {
        var entity = authorizationService.GetAccessibleCustomStepsQuery()
            .FirstOrDefault(x => x.Name == name || x.DisplayName == name);

        return entity != null ? MapToModel(entity) : null;
    }

    /// <summary>
    /// Execute a custom step on specified runners via SignalR
    /// </summary>
    public async Task<IEnumerable<RunnerCustomStepResult>> Execute(Guid customStepId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var results = new List<RunnerCustomStepResult>();
        
        // Get the custom step with decrypted values
        var customStep = await GetForExecutionAsync(customStepId);
        
        if (customStep == null)
        {
            throw new InvalidOperationException($"Custom step {customStepId} not found");
        }

        foreach (var runnerId in runnerIds)
        {
            RunnerCustomStepResult result;
            
            try
            {
                var runner = GetRunner(runnerId, userId);
                
                if (runner == null)
                {
                    logger.Warning("Runner {RunnerId} not found for user {UserId}", runnerId, userId);
                    result = new RunnerCustomStepResult
                    {
                        CustomStepId = customStepId,
                        RunnerId = runnerId,
                        Success = false,
                        Error = "Runner not found",
                        Started = DateTime.UtcNow,
                        Finished = DateTime.UtcNow
                    };
                }
                else if (string.IsNullOrEmpty(runner.ConnectionId))
                {
                    logger.Warning("Runner {RunnerId} ({RunnerName}) is not connected", runnerId, runner.Name);
                    result = new RunnerCustomStepResult
                    {
                        CustomStepId = customStepId,
                        RunnerId = runnerId,
                        Success = false,
                        Error = "Runner is not connected",
                        Started = DateTime.UtcNow,
                        Finished = DateTime.UtcNow
                    };
                }
                else
                {
                    var client = automatonHub.Clients.Client(runner.ConnectionId);
                    
                    logger.Information("Executing custom step {CustomStepId} ({CustomStepName}) on runner {RunnerId} ({RunnerName})", 
                        customStepId, customStep.Name, runnerId, runner.Name);

                    // Map to runner payload format
                    var payload = new
                    {
                        Id = customStep.Id,
                        Name = customStep.Name,
                        Code = customStep.Definition.Code,
                        Language = customStep.Definition.Language,
                        CodeInputVariables = customStep.Definition.CodeInputVariables,
                        CodeOutputVariables = customStep.Definition.CodeOutputVariables
                    };

                    var response = await client.InvokeAsync<CustomStepExecutionResponse>(
                        AutomatonHubMethods.RunCustomStep, 
                        payload, 
                        cancellationToken);

                    result = new RunnerCustomStepResult
                    {
                        CustomStepId = customStepId,
                        RunnerId = runnerId,
                        Success = response.Success,
                        Output = response.Output,
                        Error = response.Error,
                        Started = response.Started,
                        Finished = response.Finished,
                        OutputVariables = response.OutputVariables
                    };

                    logger.Information("Custom step {CustomStepId} completed on runner {RunnerId} with status: {Success}", 
                        customStepId, runnerId, response.Success ? "Success" : "Failed");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error executing custom step {CustomStepId} on runner {RunnerId}", customStepId, runnerId);
                
                result = new RunnerCustomStepResult
                {
                    CustomStepId = customStepId,
                    RunnerId = runnerId,
                    Success = false,
                    Error = $"Execution failed: {ex.Message}",
                    Started = DateTime.UtcNow,
                    Finished = DateTime.UtcNow
                };
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Get custom step with decrypted sensitive variables for UI editing
    /// </summary>
    public async Task<CustomStep> GetForEditingAsync(Guid id)
    {
        var stepEntity = authorizationService.GetAccessibleCustomStepsQuery()
            .SingleOrDefault(x => x.Id == id);

        if (stepEntity == null)
        {
            return null;
        }

        var step = MapToModel(stepEntity);

        // ✅ Decrypt sensitive variables for UI display/editing
        await DecryptSensitiveVariablesAsync(step.Definition);

        return step;
    }

    /// <summary>
    /// Get custom step with decrypted sensitive variables for execution.
    /// Use this method when preparing to send step to a runner.
    /// </summary>
    public async Task<CustomStep?> GetForExecutionAsync(Guid id)
    {
        var stepEntity = authorizationService.GetAccessibleCustomStepsQuery()
            .SingleOrDefault(x => x.Id == id);
        
        if (stepEntity == null)
        {
            return null;
        }

        var step = MapToModel(stepEntity);

        // ✅ Decrypt sensitive variables for execution
        await DecryptSensitiveVariablesAsync(step.Definition);

        return step;
    }

    /// <summary>
    /// Get custom step by name with decrypted sensitive variables for execution.
    /// Use this method when preparing to send step to a runner.
    /// </summary>
    public async Task<CustomStep?> GetByNameForExecutionAsync(string name)
    {
        var entity = authorizationService.GetAccessibleCustomStepsQuery()
            .FirstOrDefault(x => x.Name == name);

        if (entity == null)
        {
            return null;
        }

        var step = MapToModel(entity);

        // ✅ Decrypt sensitive variables for execution
        await DecryptSensitiveVariablesAsync(step.Definition);

        return step;
    }

    private Entities.Runner GetRunner(Guid id, Guid userId)
    {
        var runner = dataContext.Runners.SingleOrDefault(x => x.Id == id && x.RunnerUsers.Any(x => x.UserId == userId));

        // Reload to get current ConnectionId (same pattern as ScheduleService)
        if (runner != null)
        {
            dataContext.Entry(runner).Reload();
        }

        return runner;
    }

    #region Sensitive Variables Handling

    /// <summary>
    /// Process sensitive variables: encrypt and replace with references
    /// Called during Update operations
    /// </summary>
    private async Task ProcessSensitiveVariablesAsync(CustomStepDefinition definition, Guid customStepId)
    {
        // Process CodeInputVariables
        await ProcessVariableListAsync(definition.CodeInputVariables, customStepId, SensitiveVariableScope.Input);

        // Process CodeOutputVariables
        await ProcessVariableListAsync(definition.CodeOutputVariables, customStepId, SensitiveVariableScope.Output);
    }

    private async Task ProcessVariableListAsync(
        IList<Automaton.Core.Models.StepVariable> variables,
        Guid customStepId,
        string scope)
    {
        if (variables == null) return;

        foreach (var variable in variables)
        {
            if (variable.IsSensitive && !string.IsNullOrEmpty(variable.Value))
            {
                // Check if value is already a reference
                if (!IsSensitiveReference(variable.Value))
                {
                    try
                    {
                        // Encrypt and store in SensitiveVariables table
                        var sensitiveVar = await sensitiveVariableService.CreateOrUpdateSensitiveVariableAsync(
                            userId,
                            variable.Name,
                            variable.Value,
                            SensitiveVariableResourceType.CustomStep,
                            customStepId,
                            scope,
                            variable.Description);

                        // Replace value with reference
                        variable.Value = $"${{sensitive:{sensitiveVar.Id}}}";

                        logger.Information("Encrypted sensitive variable '{Name}' for custom step {CustomStepId}", 
                            variable.Name, customStepId);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Failed to encrypt sensitive variable '{Name}' for custom step {CustomStepId}", 
                            variable.Name, customStepId);
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Decrypt sensitive variables before execution
    /// </summary>
    private async Task DecryptSensitiveVariablesAsync(CustomStepDefinition definition)
    {
        if (definition == null) return;

        await DecryptVariableListAsync(definition.CodeInputVariables);
        await DecryptVariableListAsync(definition.CodeOutputVariables);
    }

    private async Task DecryptVariableListAsync(IList<Automaton.Core.Models.StepVariable> variables)
    {
        if (variables == null) return;

        foreach (var variable in variables)
        {
            if (variable.IsSensitive && IsSensitiveReference(variable.Value))
            {
                var decryptedValue = await GetDecryptedSensitiveValueAsync(variable.Value);
                if (decryptedValue != null)
                {
                    variable.Value = decryptedValue;
                    logger.Information("Decrypted sensitive variable '{Name}' for execution", variable.Name);
                }
                else
                {
                    logger.Warning("Failed to decrypt sensitive variable '{Name}', using empty value", variable.Name);
                    variable.Value = string.Empty;
                }
            }
        }
    }

    private bool IsSensitiveReference(string? value)
    {
        return value?.StartsWith("${sensitive:") == true && value.EndsWith("}");
    }

    private async Task<string?> GetDecryptedSensitiveValueAsync(string reference)
    {
        try
        {
            // Extract GUID from "${sensitive:guid}"
            var guidPart = reference.Substring(12, reference.Length - 13);
            if (Guid.TryParse(guidPart, out var sensitiveVarId))
            {
                return await sensitiveVariableService.GetDecryptedValueAsync(sensitiveVarId, userId);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to decrypt sensitive reference {Reference}", reference);
        }
        return null;
    }

    /// <summary>
    /// Delete all sensitive variables associated with a custom step
    /// </summary>
    private async Task DeleteSensitiveVariablesAsync(Guid customStepId)
    {
        try
        {
            var sensitiveVars = await sensitiveVariableService.GetSensitiveVariablesForResourceAsync(
                SensitiveVariableResourceType.CustomStep,
                customStepId,
                userId);

            foreach (var sensitiveVar in sensitiveVars)
            {
                await sensitiveVariableService.DeleteSensitiveVariableAsync(sensitiveVar.Id, userId);
            }

            logger.Information("Deleted {Count} sensitive variables for custom step {CustomStepId}", 
                sensitiveVars.Count, customStepId);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to delete sensitive variables for custom step {CustomStepId}", customStepId);
            // Don't throw - allow step deletion to proceed
        }
    }

    #endregion

    private static CustomStep MapToModel(Entities.CustomStep entity)
    {
        return MapToModel(entity, null);
    }

    private static CustomStep MapToModel(Entities.CustomStep entity, Dictionary<Guid, Entities.WebApp>? webAppLookup)
    {
        if (entity == null) return null;

        // Look up WebApp info if available
        Guid? webAppId = null;
        string? webAppName = null;

        if (webAppLookup != null && webAppLookup.TryGetValue(entity.Id, out var webApp))
        {
            webAppId = webApp.Id;
            webAppName = !string.IsNullOrEmpty(webApp.DisplayName) ? webApp.DisplayName : webApp.Name;
        }

        return new CustomStep
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            Icon = entity.Icon,
            Category = entity.Category,
            MoreInfo = entity.MoreInfo,
            VisibleInExplorer = entity.VisibleInExplorer,
            Created = entity.Created,
            Updated = entity.Updated,
            WebAppId = webAppId,
            WebAppName = webAppName,
            Definition = JsonSerializer.Deserialize<CustomStepDefinition>(entity.Definition ?? "{}")
        };
    }
}
