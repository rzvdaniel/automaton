using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing App-Step relationships
/// </summary>
public class AppStepsService
{
    private readonly ApplicationDbContext dataContext;
    private readonly UserContextService userContextService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly Serilog.ILogger logger;

    private const string DefaultIcon = "code";
    private const string DefaultGroup = "App Steps";

    private Guid UserId => userContextService.GetUserId();

    public AppStepsService(
        ApplicationDbContext dataContext,
        UserContextService userContextService,
        ResourceAuthorizationService authorizationService)
    {
        this.dataContext = dataContext;
        this.userContextService = userContextService;
        this.authorizationService = authorizationService;
        this.logger = Serilog.Log.ForContext<AppStepsService>();
    }

    /// <summary>
    /// Create a new step and automatically link it to the app
    /// </summary>
    public async Task<LinkedStepInfo> CreateStepForAppAsync(Guid appId, string stepName)
    {
        logger.Information("Creating step '{StepName}' for app {AppId}", stepName, appId);

        // Verify user has access to this app
        var hasAccess = await authorizationService.GetAccessibleAppsQuery()
            .AnyAsync(a => a.Id == appId);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to create step for unauthorized app {AppId}", UserId, appId);
            throw new UnauthorizedAccessException("You do not have access to this app");
        }

        // Create the custom step (linked to app via WebAppSteps table)
        var stepEntity = new Entities.CustomStep
        {
            Id = Guid.NewGuid(),
            Name = stepName,
            DisplayName = stepName,
            Description = string.Empty,
            MoreInfo = string.Empty,
            Icon = DefaultIcon,
            Category = DefaultGroup,
            VisibleInExplorer = false, // App steps are not visible in Flow designer
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            Definition = JsonSerializer.Serialize(new CustomStepDefinition())
        };

        dataContext.CustomSteps.Add(stepEntity);

        // Add user ownership
        var stepUser = new Entities.CustomStepUser
        {
            CustomStepId = stepEntity.Id,
            UserId = UserId
        };
        dataContext.CustomStepUsers.Add(stepUser);

        // Link to the app - this is now the source of truth for app association
        var link = new Entities.WebAppStep
        {
            WebAppId = appId,
            CustomStepId = stepEntity.Id,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = UserId
        };
        dataContext.WebAppSteps.Add(link);

        await dataContext.SaveChangesAsync();

        logger.Information("Successfully created step {StepId} '{StepName}' and linked to app {AppId}", 
            stepEntity.Id, stepName, appId);

        return MapToLinkedStepInfo(stepEntity);
    }

    /// <summary>
    /// Get steps linked to an app
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetLinkedStepsAsync(Guid appId)
    {
        logger.Debug("Getting linked steps for app {AppId}", appId);

        // Verify user has access to this app
        var hasAccess = await authorizationService.GetAccessibleAppsQuery()
            .AnyAsync(a => a.Id == appId);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to access steps for unauthorized app {AppId}", UserId, appId);
            return new List<LinkedStepInfo>();
        }

        var linkedSteps = await dataContext.WebAppSteps
            .Where(was => was.WebAppId == appId)
            .Include(was => was.CustomStep)
            .Select(was => was.CustomStep)
            .ToListAsync();

        var result = new List<LinkedStepInfo>();

        foreach (var step in linkedSteps)
        {
            var stepInfo = MapToLinkedStepInfo(step);
            result.Add(stepInfo);
        }

        logger.Debug("Found {Count} linked steps for app {AppId}", result.Count, appId);
        return result;
    }

    /// <summary>
    /// Get available steps that can be linked (not already linked)
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetAvailableStepsAsync(Guid appId)
    {
        logger.Debug("Getting available steps for app {AppId}", appId);

        // Verify user has access to this app
        var hasAccess = await authorizationService.GetAccessibleAppsQuery()
            .AnyAsync(a => a.Id == appId);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to access steps for unauthorized app {AppId}", UserId, appId);
            return new List<LinkedStepInfo>();
        }

        // Get all accessible steps that are not linked
        var availableSteps = await authorizationService.GetAccessibleCustomStepsQuery()
            .Where(s => !dataContext.WebAppSteps
                .Where(was => was.WebAppId == appId)
                .Select(was => was.CustomStepId)
                .Contains(s.Id))
            .ToListAsync();

        var result = new List<LinkedStepInfo>();

        foreach (var step in availableSteps)
        {
            var stepInfo = MapToLinkedStepInfo(step);
            result.Add(stepInfo);
        }

        logger.Debug("Found {Count} available steps for app {AppId}", result.Count, appId);
        return result;
    }

    /// <summary>
    /// Link a step to an app
    /// </summary>
    public async Task LinkStepAsync(Guid appId, Guid stepId)
    {
        logger.Information("Linking step {StepId} to app {AppId}", stepId, appId);

        // Verify user has access to both app and step
        var hasAppAccess = await authorizationService.GetAccessibleAppsQuery()
            .AnyAsync(a => a.Id == appId);
        var hasStepAccess = await authorizationService.GetAccessibleCustomStepsQuery()
            .AnyAsync(s => s.Id == stepId);

        if (!hasAppAccess)
        {
            logger.Warning("User {UserId} attempted to link step to unauthorized app {AppId}", UserId, appId);
            throw new UnauthorizedAccessException("You do not have access to this app");
        }

        if (!hasStepAccess)
        {
            logger.Warning("User {UserId} attempted to link unauthorized step {StepId}", UserId, stepId);
            throw new UnauthorizedAccessException("You do not have access to this step");
        }

        // Check if already linked
        var existingLink = await dataContext.WebAppSteps
            .FirstOrDefaultAsync(was => was.WebAppId == appId && was.CustomStepId == stepId);

        if (existingLink != null)
        {
            logger.Debug("Step {StepId} is already linked to app {AppId}", stepId, appId);
            return; // Already linked, nothing to do
        }

        // Create link
        var link = new Entities.WebAppStep
        {
            WebAppId = appId,
            CustomStepId = stepId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = UserId
        };

        dataContext.WebAppSteps.Add(link);
        await dataContext.SaveChangesAsync();

        logger.Information("Successfully linked step {StepId} to app {AppId}", stepId, appId);
    }

    /// <summary>
    /// Link multiple steps to an app in a single operation
    /// </summary>
    public async Task LinkStepsAsync(Guid appId, IEnumerable<Guid> stepIds)
    {
        var stepIdList = stepIds.ToList();
        logger.Information("Linking {Count} steps to app {AppId}", stepIdList.Count, appId);

        // Verify user has access to the app
        var hasAppAccess = await authorizationService.GetAccessibleAppsQuery()
            .AnyAsync(a => a.Id == appId);

        if (!hasAppAccess)
        {
            logger.Warning("User {UserId} attempted to link steps to unauthorized app {AppId}", UserId, appId);
            throw new UnauthorizedAccessException("You do not have access to this app");
        }

        // Get accessible step IDs
        var accessibleStepIds = await authorizationService.GetAccessibleCustomStepsQuery()
            .Where(s => stepIdList.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync();

        // Check for unauthorized steps
        var unauthorizedSteps = stepIdList.Except(accessibleStepIds).ToList();
        if (unauthorizedSteps.Any())
        {
            logger.Warning("User {UserId} attempted to link unauthorized steps: {StepIds}", UserId, string.Join(", ", unauthorizedSteps));
            throw new UnauthorizedAccessException($"You do not have access to some of the selected steps");
        }

        // Get already linked step IDs
        var alreadyLinkedIds = await dataContext.WebAppSteps
            .Where(was => was.WebAppId == appId && stepIdList.Contains(was.CustomStepId))
            .Select(was => was.CustomStepId)
            .ToListAsync();

        // Filter to only new links
        var newStepIds = stepIdList.Except(alreadyLinkedIds).ToList();

        if (!newStepIds.Any())
        {
            logger.Debug("All {Count} steps are already linked to app {AppId}", stepIdList.Count, appId);
            return;
        }

        // Create links for new steps
        var links = newStepIds.Select(stepId => new Entities.WebAppStep
        {
            WebAppId = appId,
            CustomStepId = stepId,
            LinkedAt = DateTime.UtcNow,
            LinkedBy = UserId
        }).ToList();

        dataContext.WebAppSteps.AddRange(links);
        await dataContext.SaveChangesAsync();

        logger.Information("Successfully linked {NewCount} new steps to app {AppId} ({SkippedCount} already linked)", 
            newStepIds.Count, appId, alreadyLinkedIds.Count);
    }

    /// <summary>
    /// Unlink a step from an app
    /// </summary>
    public async Task UnlinkStepAsync(Guid appId, Guid stepId)
    {
        logger.Information("Unlinking step {StepId} from app {AppId}", stepId, appId);

        // Verify user has access to the app
        var hasAccess = await authorizationService.GetAccessibleAppsQuery()
            .AnyAsync(a => a.Id == appId);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to unlink step from unauthorized app {AppId}", UserId, appId);
            throw new UnauthorizedAccessException("You do not have access to this app");
        }

        var link = await dataContext.WebAppSteps
            .FirstOrDefaultAsync(was => was.WebAppId == appId && was.CustomStepId == stepId);

        if (link == null)
        {
            logger.Debug("Step {StepId} was not linked to app {AppId}", stepId, appId);
            return; // Not linked, nothing to do
        }

        dataContext.WebAppSteps.Remove(link);
        await dataContext.SaveChangesAsync();

        logger.Information("Successfully unlinked step {StepId} from app {AppId}", stepId, appId);
    }

    /// <summary>
    /// Map CustomStep entity to LinkedStepInfo model
    /// </summary>
    private LinkedStepInfo MapToLinkedStepInfo(Entities.CustomStep step)
    {
        var definition = JsonSerializer.Deserialize<CustomStepDefinition>(step.Definition);

        var inputVariables = definition?.CodeInputVariables?.Select(v => new StepVariableInfo
        {
            Name = v.Name ?? string.Empty,
            Description = v.Description,
            Type = "String" // StepVariable doesn't have Type property
        }).ToList() ?? new List<StepVariableInfo>();

        var outputVariables = definition?.CodeOutputVariables?.Select(v => new StepVariableInfo
        {
            Name = v.Name ?? string.Empty,
            Description = v.Description,
            Type = "String" // StepVariable doesn't have Type property
        }).ToList() ?? new List<StepVariableInfo>();

        return new LinkedStepInfo
        {
            Id = step.Id,
            Name = step.Name,
            DisplayName = step.DisplayName,
            Description = step.Description,
            Language = definition?.Language ?? "Python",
            Version = definition?.Version ?? 1,
            Created = step.Created,
            Updated = step.Updated,
            InputCount = inputVariables.Count,
            OutputCount = outputVariables.Count,
            InputVariables = inputVariables,
            OutputVariables = outputVariables
        };
    }
}
