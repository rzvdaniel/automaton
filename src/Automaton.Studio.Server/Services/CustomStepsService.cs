using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
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
        ResourceAuthorizationService authorizationService
    )
    {
        this.dataContext = dataContext;
        this.recentActivityService = recentActivityService;
        this.automatonHub = automatonHub;
        this.authorizationService = authorizationService;
        this.userId = userContextService.GetUserId();
        this.logger = Log.ForContext<CustomStepsService>();
    }

    public IEnumerable<CustomStep> List()
    {
        // Use authorization service to get accessible custom steps (personal + project-based)
        var entities = authorizationService.GetAccessibleCustomStepsQuery();
        var steps = entities.Select(MapToModel);

        return steps;
    }

    public CustomStep Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var stepEntity = authorizationService.GetAccessibleCustomStepsQuery()
            .SingleOrDefault(x => x.Id == id);
        var step = MapToModel(stepEntity);
 
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
    /// Execute a custom step on specified runners via SignalR
    /// </summary>
    public async Task<IEnumerable<RunnerCustomStepResult>> Execute(Guid customStepId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var results = new List<RunnerCustomStepResult>();
        
        // Get the custom step
        var customStep = Get(customStepId);
        
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

    private static CustomStep MapToModel(Entities.CustomStep entity)
    {
        if (entity == null) return null;

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
            Definition = JsonSerializer.Deserialize<CustomStepDefinition>(entity.Definition ?? "{}")
        };
    }
}
