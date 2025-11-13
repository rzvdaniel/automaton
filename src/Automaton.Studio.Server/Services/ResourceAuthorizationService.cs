using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Centralized service for resource access authorization.
/// Handles both personal ownership and project-based access control.
/// </summary>
public class ResourceAuthorizationService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly Serilog.ILogger logger;

    private Guid UserId => userContextService.GetUserId();

    public ResourceAuthorizationService(
        ApplicationDbContext dbContext,
        UserContextService userContextService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.logger = Log.ForContext<ResourceAuthorizationService>();
    }

    #region Apps Authorization

    /// <summary>
    /// Check if user can access an app (read, view)
    /// User can access if:
    /// - They are the personal owner (via WebAppUsers)
    /// - They are a project member with any role (ProjectAdmin, Developer, AppUser, Viewer)
    /// </summary>
    public async Task<bool> CanAccessAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        return await CanAccessAppAsync(appId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can access an app
    /// </summary>
    public async Task<bool> CanAccessAppAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.WebAppUsers
            .AnyAsync(u => u.WebAppId == appId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership
        var hasProjectAccess = await dbContext.ProjectApps
            .Where(pa => pa.WebAppId == appId)
            .Join(
                dbContext.ProjectMembers,
                pa => pa.ProjectId,
                pm => pm.ProjectId,
                (pa, pm) => new { pa, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive, cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can update/modify an app
    /// User can update if:
    /// - They are the personal owner (via WebAppUsers)
    /// - They are a project member with ProjectAdmin or Developer role
    /// </summary>
    public async Task<bool> CanUpdateAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        return await CanUpdateAppAsync(appId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can update an app
    /// </summary>
    public async Task<bool> CanUpdateAppAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.WebAppUsers
            .AnyAsync(u => u.WebAppId == appId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership with appropriate role
        var hasProjectAccess = await dbContext.ProjectApps
            .Where(pa => pa.WebAppId == appId)
            .Join(
                dbContext.ProjectMembers,
                pa => pa.ProjectId,
                pm => pm.ProjectId,
                (pa, pm) => new { pa, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin || x.pm.Role == ProjectRole.Developer),
                      cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can delete an app
    /// User can delete only if:
    /// - They are the personal owner AND the app is NOT part of any project
    /// </summary>
    public async Task<bool> CanDeleteAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        return await CanDeleteAppAsync(appId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can delete an app
    /// </summary>
    public async Task<bool> CanDeleteAppAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.WebAppUsers
            .AnyAsync(u => u.WebAppId == appId && u.UserId == userId, cancellationToken);

        if (!hasPersonalAccess)
        {
            return false;
        }

        // Check if app is part of any project
        var isPartOfProject = await dbContext.ProjectApps
            .AnyAsync(pa => pa.WebAppId == appId, cancellationToken);

        return !isPartOfProject;
    }

    /// <summary>
    /// Check if user can execute an app
    /// User can execute if they have access and:
    /// - Personal owner
    /// - Project member with ProjectAdmin, Developer, or AppUser role (not Viewer)
    /// </summary>
    public async Task<bool> CanExecuteAppAsync(Guid appId, CancellationToken cancellationToken = default)
    {
        return await CanExecuteAppAsync(appId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can execute an app
    /// </summary>
    public async Task<bool> CanExecuteAppAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.WebAppUsers
            .AnyAsync(u => u.WebAppId == appId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership with execute permission
        var hasProjectAccess = await dbContext.ProjectApps
            .Where(pa => pa.WebAppId == appId)
            .Join(
                dbContext.ProjectMembers,
                pa => pa.ProjectId,
                pm => pm.ProjectId,
                (pa, pm) => new { pa, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin ||
                           x.pm.Role == ProjectRole.Developer ||
                           x.pm.Role == ProjectRole.AppUser),
                      cancellationToken);

        return hasProjectAccess;
    }

    #endregion

    #region Flows Authorization

    /// <summary>
    /// Check if user can access a flow
    /// </summary>
    public async Task<bool> CanAccessFlowAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        return await CanAccessFlowAsync(flowId, UserId, cancellationToken);
    }

    public async Task<bool> CanAccessFlowAsync(Guid flowId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.FlowUsers
            .AnyAsync(u => u.FlowId == flowId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership
        var hasProjectAccess = await dbContext.ProjectFlows
            .Where(pf => pf.FlowId == flowId)
            .Join(
                dbContext.ProjectMembers,
                pf => pf.ProjectId,
                pm => pm.ProjectId,
                (pf, pm) => new { pf, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive, cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can update a flow
    /// </summary>
    public async Task<bool> CanUpdateFlowAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        return await CanUpdateFlowAsync(flowId, UserId, cancellationToken);
    }

    public async Task<bool> CanUpdateFlowAsync(Guid flowId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.FlowUsers
            .AnyAsync(u => u.FlowId == flowId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership with appropriate role
        var hasProjectAccess = await dbContext.ProjectFlows
            .Where(pf => pf.FlowId == flowId)
            .Join(
                dbContext.ProjectMembers,
                pf => pf.ProjectId,
                pm => pm.ProjectId,
                (pf, pm) => new { pf, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin || x.pm.Role == ProjectRole.Developer),
                      cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can delete a flow
    /// </summary>
    public async Task<bool> CanDeleteFlowAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        return await CanDeleteFlowAsync(flowId, UserId, cancellationToken);
    }

    public async Task<bool> CanDeleteFlowAsync(Guid flowId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.FlowUsers
            .AnyAsync(u => u.FlowId == flowId && u.UserId == userId, cancellationToken);

        if (!hasPersonalAccess)
        {
            return false;
        }

        // Check if flow is part of any project
        var isPartOfProject = await dbContext.ProjectFlows
            .AnyAsync(pf => pf.FlowId == flowId, cancellationToken);

        return !isPartOfProject;
    }

    /// <summary>
    /// Check if user can execute a flow
    /// </summary>
    public async Task<bool> CanExecuteFlowAsync(Guid flowId, CancellationToken cancellationToken = default)
    {
        return await CanExecuteFlowAsync(flowId, UserId, cancellationToken);
    }

    public async Task<bool> CanExecuteFlowAsync(Guid flowId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.FlowUsers
            .AnyAsync(u => u.FlowId == flowId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership with execute permission (ProjectAdmin or Developer only)
        var hasProjectAccess = await dbContext.ProjectFlows
            .Where(pf => pf.FlowId == flowId)
            .Join(
                dbContext.ProjectMembers,
                pf => pf.ProjectId,
                pm => pm.ProjectId,
                (pf, pm) => new { pf, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin || x.pm.Role == ProjectRole.Developer),
                      cancellationToken);

        return hasProjectAccess;
    }

    #endregion

    #region Custom Steps Authorization

    /// <summary>
    /// Check if user can access a custom step
    /// </summary>
    public async Task<bool> CanAccessCustomStepAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        return await CanAccessCustomStepAsync(stepId, UserId, cancellationToken);
    }

    public async Task<bool> CanAccessCustomStepAsync(Guid stepId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.CustomStepUsers
            .AnyAsync(u => u.CustomStepId == stepId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership
        var hasProjectAccess = await dbContext.ProjectCustomSteps
            .Where(pcs => pcs.CustomStepId == stepId)
            .Join(
                dbContext.ProjectMembers,
                pcs => pcs.ProjectId,
                pm => pm.ProjectId,
                (pcs, pm) => new { pcs, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive, cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can update a custom step
    /// </summary>
    public async Task<bool> CanUpdateCustomStepAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        return await CanUpdateCustomStepAsync(stepId, UserId, cancellationToken);
    }

    public async Task<bool> CanUpdateCustomStepAsync(Guid stepId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.CustomStepUsers
            .AnyAsync(u => u.CustomStepId == stepId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership with appropriate role
        var hasProjectAccess = await dbContext.ProjectCustomSteps
            .Where(pcs => pcs.CustomStepId == stepId)
            .Join(
                dbContext.ProjectMembers,
                pcs => pcs.ProjectId,
                pm => pm.ProjectId,
                (pcs, pm) => new { pcs, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin || x.pm.Role == ProjectRole.Developer),
                      cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can delete a custom step
    /// </summary>
    public async Task<bool> CanDeleteCustomStepAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        return await CanDeleteCustomStepAsync(stepId, UserId, cancellationToken);
    }

    public async Task<bool> CanDeleteCustomStepAsync(Guid stepId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.CustomStepUsers
            .AnyAsync(u => u.CustomStepId == stepId && u.UserId == userId, cancellationToken);

        if (!hasPersonalAccess)
        {
            return false;
        }

        // Check if step is part of any project
        var isPartOfProject = await dbContext.ProjectCustomSteps
            .AnyAsync(pcs => pcs.CustomStepId == stepId, cancellationToken);

        return !isPartOfProject;
    }

    /// <summary>
    /// Check if user can execute a custom step
    /// </summary>
    public async Task<bool> CanExecuteCustomStepAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        return await CanExecuteCustomStepAsync(stepId, UserId, cancellationToken);
    }

    public async Task<bool> CanExecuteCustomStepAsync(Guid stepId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.CustomStepUsers
            .AnyAsync(u => u.CustomStepId == stepId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership with execute permission
        var hasProjectAccess = await dbContext.ProjectCustomSteps
            .Where(pcs => pcs.CustomStepId == stepId)
            .Join(
                dbContext.ProjectMembers,
                pcs => pcs.ProjectId,
                pm => pm.ProjectId,
                (pcs, pm) => new { pcs, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin || x.pm.Role == ProjectRole.Developer),
                      cancellationToken);

        return hasProjectAccess;
    }

    #endregion

    #region Runners Authorization

    /// <summary>
    /// Check if user can access a runner
    /// </summary>
    public async Task<bool> CanAccessRunnerAsync(Guid runnerId, CancellationToken cancellationToken = default)
    {
        return await CanAccessRunnerAsync(runnerId, UserId, cancellationToken);
    }

    public async Task<bool> CanAccessRunnerAsync(Guid runnerId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.RunnerUsers
            .AnyAsync(u => u.RunnerId == runnerId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership
        var hasProjectAccess = await dbContext.ProjectRunners
            .Where(pr => pr.RunnerId == runnerId)
            .Join(
                dbContext.ProjectMembers,
                pr => pr.ProjectId,
                pm => pm.ProjectId,
                (pr, pm) => new { pr, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive, cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can update a runner
    /// </summary>
    public async Task<bool> CanUpdateRunnerAsync(Guid runnerId, CancellationToken cancellationToken = default)
    {
        return await CanUpdateRunnerAsync(runnerId, UserId, cancellationToken);
    }

    public async Task<bool> CanUpdateRunnerAsync(Guid runnerId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.RunnerUsers
            .AnyAsync(u => u.RunnerId == runnerId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        // Check project membership with appropriate role
        var hasProjectAccess = await dbContext.ProjectRunners
            .Where(pr => pr.RunnerId == runnerId)
            .Join(
                dbContext.ProjectMembers,
                pr => pr.ProjectId,
                pm => pm.ProjectId,
                (pr, pm) => new { pr, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin || x.pm.Role == ProjectRole.Developer),
                      cancellationToken);

        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can delete a runner
    /// </summary>
    public async Task<bool> CanDeleteRunnerAsync(Guid runnerId, CancellationToken cancellationToken = default)
    {
        return await CanDeleteRunnerAsync(runnerId, UserId, cancellationToken);
    }

    public async Task<bool> CanDeleteRunnerAsync(Guid runnerId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var hasPersonalAccess = await dbContext.RunnerUsers
            .AnyAsync(u => u.RunnerId == runnerId && u.UserId == userId, cancellationToken);

        if (!hasPersonalAccess)
        {
            return false;
        }

        // Check if runner is part of any project
        var isPartOfProject = await dbContext.ProjectRunners
            .AnyAsync(pr => pr.RunnerId == runnerId, cancellationToken);

        return !isPartOfProject;
    }

    #endregion

    #region Query Helpers

    /// <summary>
    /// Get IQueryable for apps that the user can access
    /// </summary>
    public IQueryable<Entities.WebApp> GetAccessibleAppsQuery()
    {
        return GetAccessibleAppsQuery(UserId);
    }

    public IQueryable<Entities.WebApp> GetAccessibleAppsQuery(Guid userId)
    {
        // Apps where user is personal owner
        var personalApps = dbContext.WebApps
            .Where(a => a.WebAppUsers.Any(u => u.UserId == userId));

        // Apps where user is project member
        var projectAppIds = dbContext.ProjectApps
            .Where(pa => pa.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
            .Select(pa => pa.WebAppId);

        var projectApps = dbContext.WebApps
            .Where(a => projectAppIds.Contains(a.Id));

        return personalApps.Union(projectApps);
    }

    /// <summary>
    /// Get IQueryable for flows that the user can access
    /// </summary>
    public IQueryable<Entities.Flow> GetAccessibleFlowsQuery()
    {
        return GetAccessibleFlowsQuery(UserId);
    }

    public IQueryable<Entities.Flow> GetAccessibleFlowsQuery(Guid userId)
    {
        // Flows where user is personal owner
        var personalFlows = dbContext.Flows
            .Where(f => f.FlowUsers.Any(u => u.UserId == userId));

        // Flows where user is project member
        var projectFlowIds = dbContext.ProjectFlows
            .Where(pf => pf.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
            .Select(pf => pf.FlowId);

        var projectFlows = dbContext.Flows
            .Where(f => projectFlowIds.Contains(f.Id));

        return personalFlows.Union(projectFlows);
    }

    /// <summary>
    /// Get IQueryable for custom steps that the user can access
    /// </summary>
    public IQueryable<Entities.CustomStep> GetAccessibleCustomStepsQuery()
    {
        return GetAccessibleCustomStepsQuery(UserId);
    }

    public IQueryable<Entities.CustomStep> GetAccessibleCustomStepsQuery(Guid userId)
    {
        // Steps where user is personal owner
        var personalSteps = dbContext.CustomSteps
            .Where(cs => cs.CustomStepUsers.Any(u => u.UserId == userId));

        // Steps where user is project member
        var projectStepIds = dbContext.ProjectCustomSteps
            .Where(pcs => pcs.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
            .Select(pcs => pcs.CustomStepId);

        var projectSteps = dbContext.CustomSteps
            .Where(cs => projectStepIds.Contains(cs.Id));

        return personalSteps.Union(projectSteps);
    }

    /// <summary>
    /// Get IQueryable for runners that the user can access
    /// </summary>
    public IQueryable<Entities.Runner> GetAccessibleRunnersQuery()
    {
        return GetAccessibleRunnersQuery(UserId);
    }

    public IQueryable<Entities.Runner> GetAccessibleRunnersQuery(Guid userId)
    {
        // Runners where user is personal owner
        var personalRunners = dbContext.Runners
            .Where(r => r.RunnerUsers.Any(u => u.UserId == userId));

        // Runners where user is project member
        var projectRunnerIds = dbContext.ProjectRunners
            .Where(pr => pr.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
            .Select(pr => pr.RunnerId);

        var projectRunners = dbContext.Runners
            .Where(r => projectRunnerIds.Contains(r.Id));

        return personalRunners.Union(projectRunners);
    }

    #endregion
}
