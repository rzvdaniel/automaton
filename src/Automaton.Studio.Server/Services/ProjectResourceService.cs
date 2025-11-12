using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing project resources (apps, flows, runners, custom steps)
/// </summary>
public class ProjectResourceService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly ProjectService projectService;
    private readonly Serilog.ILogger logger;

    private Guid UserId => userContextService.GetUserId();

    public ProjectResourceService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        ProjectService projectService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.projectService = projectService;
        this.logger = Serilog.Log.ForContext<ProjectResourceService>();
    }

    #region Apps

    /// <summary>
    /// Add an app to a project. Only ProjectAdmin and Developer can add apps.
    /// </summary>
    public async Task<ProjectAppDto?> AddAppToProjectAsync(Guid projectId, Guid appId, CancellationToken cancellationToken = default)
    {
        // Check permissions
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to add app to project {ProjectId} without permission", UserId, projectId);
            return null;
        }

        // Check if app exists and user has access
        var app = await dbContext.WebApps
            .FirstOrDefaultAsync(a => a.Id == appId && a.WebAppUsers.Any(u => u.UserId == UserId), cancellationToken);

        if (app == null)
        {
            throw new InvalidOperationException("App not found or access denied");
        }

        // Check if already linked
        var existingLink = await dbContext.ProjectApps
            .FirstOrDefaultAsync(pa => pa.ProjectId == projectId && pa.WebAppId == appId, cancellationToken);

        if (existingLink != null)
        {
            throw new InvalidOperationException("App is already linked to this project");
        }

        var projectApp = new ProjectApp
        {
            ProjectId = projectId,
            WebAppId = appId,
            AddedAt = DateTime.UtcNow,
            AddedBy = UserId
        };

        dbContext.ProjectApps.Add(projectApp);

        // Update app scope and project
        app.Scope = AccessScope.Project;
        app.ProjectId = projectId;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Added app {AppId} to project {ProjectId} by user {UserId}", appId, projectId, UserId);

        return new ProjectAppDto
        {
            ProjectId = projectId,
            AppId = appId,
            AppName = app.Name,
            AddedAt = projectApp.AddedAt,
            AddedBy = UserId,
            AddedByName = userContextService.GetUserName()
        };
    }

    /// <summary>
    /// Remove an app from a project. Only ProjectAdmin and Developer can remove apps.
    /// </summary>
    public async Task<bool> RemoveAppFromProjectAsync(Guid projectId, Guid appId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to remove app from project {ProjectId} without permission", UserId, projectId);
            return false;
        }

        var projectApp = await dbContext.ProjectApps
            .Include(pa => pa.WebApp)
            .FirstOrDefaultAsync(pa => pa.ProjectId == projectId && pa.WebAppId == appId, cancellationToken);

        if (projectApp == null)
        {
            return false;
        }

        dbContext.ProjectApps.Remove(projectApp);

        // Revert app to personal scope
        if (projectApp.WebApp != null)
        {
            projectApp.WebApp.Scope = AccessScope.Personal;
            projectApp.WebApp.ProjectId = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Removed app {AppId} from project {ProjectId} by user {UserId}", appId, projectId, UserId);
        return true;
    }

    /// <summary>
    /// List all apps in a project
    /// </summary>
    public async Task<IEnumerable<ProjectAppDto>> ListProjectAppsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Check if user has access to this project
        var hasAccess = await dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to list apps in project {ProjectId} without access", UserId, projectId);
            return Enumerable.Empty<ProjectAppDto>();
        }

        var apps = await dbContext.ProjectApps
            .Where(pa => pa.ProjectId == projectId)
            .Include(pa => pa.WebApp)
            .Include(pa => pa.AddedByUser)
            .Select(pa => new ProjectAppDto
            {
                ProjectId = pa.ProjectId,
                AppId = pa.WebAppId,
                AppName = pa.WebApp.Name,
                AddedAt = pa.AddedAt,
                AddedBy = pa.AddedBy,
                AddedByName = pa.AddedByUser != null ? pa.AddedByUser.UserName ?? string.Empty : string.Empty
            })
            .OrderByDescending(pa => pa.AddedAt)
            .ToListAsync(cancellationToken);

        return apps;
    }

    #endregion

    #region Flows

    /// <summary>
    /// Add a flow to a project. Only ProjectAdmin and Developer can add flows.
    /// </summary>
    public async Task<ProjectFlowDto?> AddFlowToProjectAsync(Guid projectId, Guid flowId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to add flow to project {ProjectId} without permission", UserId, projectId);
            return null;
        }

        var flow = await dbContext.Flows
            .FirstOrDefaultAsync(f => f.Id == flowId && f.FlowUsers.Any(u => u.UserId == UserId), cancellationToken);

        if (flow == null)
        {
            throw new InvalidOperationException("Flow not found or access denied");
        }

        var existingLink = await dbContext.ProjectFlows
            .FirstOrDefaultAsync(pf => pf.ProjectId == projectId && pf.FlowId == flowId, cancellationToken);

        if (existingLink != null)
        {
            throw new InvalidOperationException("Flow is already linked to this project");
        }

        var projectFlow = new ProjectFlow
        {
            ProjectId = projectId,
            FlowId = flowId,
            AddedAt = DateTime.UtcNow,
            AddedBy = UserId
        };

        dbContext.ProjectFlows.Add(projectFlow);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Added flow {FlowId} to project {ProjectId} by user {UserId}", flowId, projectId, UserId);

        return new ProjectFlowDto
        {
            ProjectId = projectId,
            FlowId = flowId,
            FlowName = flow.Name,
            AddedAt = projectFlow.AddedAt,
            AddedBy = UserId,
            AddedByName = userContextService.GetUserName()
        };
    }

    /// <summary>
    /// Remove a flow from a project
    /// </summary>
    public async Task<bool> RemoveFlowFromProjectAsync(Guid projectId, Guid flowId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to remove flow from project {ProjectId} without permission", UserId, projectId);
            return false;
        }

        var projectFlow = await dbContext.ProjectFlows
            .FirstOrDefaultAsync(pf => pf.ProjectId == projectId && pf.FlowId == flowId, cancellationToken);

        if (projectFlow == null)
        {
            return false;
        }

        dbContext.ProjectFlows.Remove(projectFlow);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Removed flow {FlowId} from project {ProjectId} by user {UserId}", flowId, projectId, UserId);
        return true;
    }

    /// <summary>
    /// List all flows in a project
    /// </summary>
    public async Task<IEnumerable<ProjectFlowDto>> ListProjectFlowsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var hasAccess = await dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to list flows in project {ProjectId} without access", UserId, projectId);
            return Enumerable.Empty<ProjectFlowDto>();
        }

        var flows = await dbContext.ProjectFlows
            .Where(pf => pf.ProjectId == projectId)
            .Include(pf => pf.Flow)
            .Include(pf => pf.AddedByUser)
            .Select(pf => new ProjectFlowDto
            {
                ProjectId = pf.ProjectId,
                FlowId = pf.FlowId,
                FlowName = pf.Flow.Name,
                AddedAt = pf.AddedAt,
                AddedBy = pf.AddedBy,
                AddedByName = pf.AddedByUser != null ? pf.AddedByUser.UserName ?? string.Empty : string.Empty
            })
            .OrderByDescending(pf => pf.AddedAt)
            .ToListAsync(cancellationToken);

        return flows;
    }

    #endregion

    #region Runners

    /// <summary>
    /// Add a runner to a project. Only ProjectAdmin and Developer can add runners.
    /// </summary>
    public async Task<ProjectRunnerDto?> AddRunnerToProjectAsync(Guid projectId, Guid runnerId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to add runner to project {ProjectId} without permission", UserId, projectId);
            return null;
        }

        var runner = await dbContext.Runners
            .FirstOrDefaultAsync(r => r.Id == runnerId && r.RunnerUsers.Any(u => u.UserId == UserId), cancellationToken);

        if (runner == null)
        {
            throw new InvalidOperationException("Runner not found or access denied");
        }

        var existingLink = await dbContext.ProjectRunners
            .FirstOrDefaultAsync(pr => pr.ProjectId == projectId && pr.RunnerId == runnerId, cancellationToken);

        if (existingLink != null)
        {
            throw new InvalidOperationException("Runner is already linked to this project");
        }

        var projectRunner = new ProjectRunner
        {
            ProjectId = projectId,
            RunnerId = runnerId,
            AddedAt = DateTime.UtcNow,
            AddedBy = UserId
        };

        dbContext.ProjectRunners.Add(projectRunner);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Added runner {RunnerId} to project {ProjectId} by user {UserId}", runnerId, projectId, UserId);

        return new ProjectRunnerDto
        {
            ProjectId = projectId,
            RunnerId = runnerId,
            RunnerName = runner.Name,
            AddedAt = projectRunner.AddedAt,
            AddedBy = UserId,
            AddedByName = userContextService.GetUserName()
        };
    }

    /// <summary>
    /// Remove a runner from a project
    /// </summary>
    public async Task<bool> RemoveRunnerFromProjectAsync(Guid projectId, Guid runnerId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to remove runner from project {ProjectId} without permission", UserId, projectId);
            return false;
        }

        var projectRunner = await dbContext.ProjectRunners
            .FirstOrDefaultAsync(pr => pr.ProjectId == projectId && pr.RunnerId == runnerId, cancellationToken);

        if (projectRunner == null)
        {
            return false;
        }

        dbContext.ProjectRunners.Remove(projectRunner);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Removed runner {RunnerId} from project {ProjectId} by user {UserId}", runnerId, projectId, UserId);
        return true;
    }

    /// <summary>
    /// List all runners in a project
    /// </summary>
    public async Task<IEnumerable<ProjectRunnerDto>> ListProjectRunnersAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var hasAccess = await dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to list runners in project {ProjectId} without access", UserId, projectId);
            return Enumerable.Empty<ProjectRunnerDto>();
        }

        var runners = await dbContext.ProjectRunners
            .Where(pr => pr.ProjectId == projectId)
            .Include(pr => pr.Runner)
            .Include(pr => pr.AddedByUser)
            .Select(pr => new ProjectRunnerDto
            {
                ProjectId = pr.ProjectId,
                RunnerId = pr.RunnerId,
                RunnerName = pr.Runner.Name,
                AddedAt = pr.AddedAt,
                AddedBy = pr.AddedBy,
                AddedByName = pr.AddedByUser != null ? pr.AddedByUser.UserName ?? string.Empty : string.Empty
            })
            .OrderByDescending(pr => pr.AddedAt)
            .ToListAsync(cancellationToken);

        return runners;
    }

    #endregion

    #region Custom Steps

    /// <summary>
    /// Add a custom step to a project. Only ProjectAdmin and Developer can add steps.
    /// </summary>
    public async Task<ProjectCustomStepDto?> AddCustomStepToProjectAsync(Guid projectId, Guid customStepId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to add custom step to project {ProjectId} without permission", UserId, projectId);
            return null;
        }

        var customStep = await dbContext.CustomSteps
            .FirstOrDefaultAsync(cs => cs.Id == customStepId && cs.CustomStepUsers.Any(u => u.UserId == UserId), cancellationToken);

        if (customStep == null)
        {
            throw new InvalidOperationException("Custom step not found or access denied");
        }

        var existingLink = await dbContext.ProjectCustomSteps
            .FirstOrDefaultAsync(pcs => pcs.ProjectId == projectId && pcs.CustomStepId == customStepId, cancellationToken);

        if (existingLink != null)
        {
            throw new InvalidOperationException("Custom step is already linked to this project");
        }

        var projectCustomStep = new ProjectCustomStep
        {
            ProjectId = projectId,
            CustomStepId = customStepId,
            AddedAt = DateTime.UtcNow,
            AddedBy = UserId
        };

        dbContext.ProjectCustomSteps.Add(projectCustomStep);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Added custom step {CustomStepId} to project {ProjectId} by user {UserId}", customStepId, projectId, UserId);

        return new ProjectCustomStepDto
        {
            ProjectId = projectId,
            StepId = customStepId,
            StepName = customStep.Name,
            AddedAt = projectCustomStep.AddedAt,
            AddedBy = UserId,
            AddedByName = userContextService.GetUserName()
        };
    }

    /// <summary>
    /// Remove a custom step from a project
    /// </summary>
    public async Task<bool> RemoveCustomStepFromProjectAsync(Guid projectId, Guid customStepId, CancellationToken cancellationToken = default)
    {
        if (!await CanManageResourcesAsync(projectId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to remove custom step from project {ProjectId} without permission", UserId, projectId);
            return false;
        }

        var projectCustomStep = await dbContext.ProjectCustomSteps
            .FirstOrDefaultAsync(pcs => pcs.ProjectId == projectId && pcs.CustomStepId == customStepId, cancellationToken);

        if (projectCustomStep == null)
        {
            return false;
        }

        dbContext.ProjectCustomSteps.Remove(projectCustomStep);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Removed custom step {CustomStepId} from project {ProjectId} by user {UserId}", customStepId, projectId, UserId);
        return true;
    }

    /// <summary>
    /// List all custom steps in a project
    /// </summary>
    public async Task<IEnumerable<ProjectCustomStepDto>> ListProjectCustomStepsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var hasAccess = await dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to list custom steps in project {ProjectId} without access", UserId, projectId);
            return Enumerable.Empty<ProjectCustomStepDto>();
        }

        var customSteps = await dbContext.ProjectCustomSteps
            .Where(pcs => pcs.ProjectId == projectId)
            .Include(pcs => pcs.CustomStep)
            .Include(pcs => pcs.AddedByUser)
            .Select(pcs => new ProjectCustomStepDto
            {
                ProjectId = pcs.ProjectId,
                StepId = pcs.CustomStepId,
                StepName = pcs.CustomStep.Name,
                AddedAt = pcs.AddedAt,
                AddedBy = pcs.AddedBy,
                AddedByName = pcs.AddedByUser != null ? pcs.AddedByUser.UserName ?? string.Empty : string.Empty
            })
            .OrderByDescending(pcs => pcs.AddedAt)
            .ToListAsync(cancellationToken);

        return customSteps;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Check if current user can manage resources (add/remove apps/flows/runners/steps)
    /// Only ProjectAdmin and Developer can manage resources
    /// </summary>
    private async Task<bool> CanManageResourcesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var membership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (membership == null)
        {
            return false;
        }

        return membership.Role == ProjectRole.ProjectAdmin || membership.Role == ProjectRole.Developer;
    }

    #endregion
}
