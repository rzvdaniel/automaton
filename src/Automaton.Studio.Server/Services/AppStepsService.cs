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
    /// Get steps linked to an app
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetLinkedStepsAsync(Guid appId)
    {
        logger.Information("Getting linked steps for app {AppId}", appId);

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
            var stepInfo = await MapToLinkedStepInfo(step);
            result.Add(stepInfo);
        }

        logger.Information("Found {Count} linked steps for app {AppId}", result.Count, appId);
        return result;
    }

    /// <summary>
    /// Get available steps that can be linked (not already linked)
    /// </summary>
    public async Task<List<LinkedStepInfo>> GetAvailableStepsAsync(Guid appId)
    {
        logger.Information("Getting available steps for app {AppId}", appId);

        // Verify user has access to this app
        var hasAccess = await authorizationService.GetAccessibleAppsQuery()
            .AnyAsync(a => a.Id == appId);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to access steps for unauthorized app {AppId}", UserId, appId);
            return new List<LinkedStepInfo>();
        }

        // Get already linked step IDs
        var linkedStepIds = await dataContext.WebAppSteps
            .Where(was => was.WebAppId == appId)
            .Select(was => was.CustomStepId)
            .ToListAsync();

        // Get all accessible steps that are not linked
        var availableSteps = await authorizationService.GetAccessibleCustomStepsQuery()
            .Where(s => !linkedStepIds.Contains(s.Id))
            .ToListAsync();

        var result = new List<LinkedStepInfo>();

        foreach (var step in availableSteps)
        {
            var stepInfo = await MapToLinkedStepInfo(step);
            result.Add(stepInfo);
        }

        logger.Information("Found {Count} available steps for app {AppId}", result.Count, appId);
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
            logger.Information("Step {StepId} is already linked to app {AppId}", stepId, appId);
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
            logger.Information("Step {StepId} was not linked to app {AppId}", stepId, appId);
            return; // Not linked, nothing to do
        }

        dataContext.WebAppSteps.Remove(link);
        await dataContext.SaveChangesAsync();

        logger.Information("Successfully unlinked step {StepId} from app {AppId}", stepId, appId);
    }

    /// <summary>
    /// Map CustomStep entity to LinkedStepInfo model
    /// </summary>
    private async Task<LinkedStepInfo> MapToLinkedStepInfo(Entities.CustomStep step)
    {
        var definition = JsonSerializer.Deserialize<CustomStepDefinition>(step.Definition);

        var inputVariables = definition?.InputVariables?.Select(v => new StepVariableInfo
        {
            Name = v.Name,
            Description = v.Description,
            Type = v.Type
        }).ToList() ?? new List<StepVariableInfo>();

        var outputVariables = definition?.OutputVariables?.Select(v => new StepVariableInfo
        {
            Name = v.Name,
            Description = v.Description,
            Type = v.Type
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
