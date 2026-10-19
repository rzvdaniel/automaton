using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Security.Claims;

namespace Automaton.Api.Services;

/// <summary>
/// Centralized service for resource access authorization.
/// Handles both personal ownership and project-based access control.
/// </summary>
public class ResourceAuthorizationService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly Serilog.ILogger logger;
    private readonly IHttpContextAccessor httpContextAccessor;

    private Guid UserId => userContextService.GetUserId();

    public ResourceAuthorizationService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.httpContextAccessor = httpContextAccessor ?? new HttpContextAccessor();
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
        if (Automaton.Core.Models.InfrastructureAppTrust.IsTrustedApp(appId) && IsTrustedInfrastructureAdministrator(userId)) return true;
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

    /// <summary>Checks app runtime use, including explicit organization assignment, without granting app administration.</summary>
    public async Task<bool> CanUseAppRuntimeAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
        => await CanAccessAppAsync(appId, userId, cancellationToken) ||
           await CanExecuteAssignedOrganizationAppAsync(appId, userId, cancellationToken);

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
        if (Automaton.Core.Models.InfrastructureAppTrust.IsTrustedApp(appId) && IsTrustedInfrastructureAdministrator(userId)) return true;
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
        if (Automaton.Core.Models.InfrastructureAppTrust.IsTrustedApp(appId)) return false;
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
        if (Automaton.Core.Models.InfrastructureAppTrust.IsTrustedApp(appId) && IsTrustedInfrastructureAdministrator(userId)) return true;
        // Check personal ownership
        var hasPersonalAccess = await dbContext.WebAppUsers
            .AnyAsync(u => u.WebAppId == appId && u.UserId == userId, cancellationToken);

        if (hasPersonalAccess)
        {
            return true;
        }

        if (await HasProjectAppExecutionAccessAsync(appId, userId, cancellationToken))
        {
            return true;
        }

        return await CanExecuteAssignedOrganizationAppAsync(appId, userId, cancellationToken);
    }

    private Task<bool> HasProjectAppExecutionAccessAsync(
        Guid appId,
        Guid userId,
        CancellationToken cancellationToken) => dbContext.ProjectApps
            .Where(pa => pa.WebAppId == appId)
            .Join(
                dbContext.ProjectMembers,
                pa => pa.ProjectId,
                pm => pm.ProjectId,
                (pa, pm) => new { pa, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive &&
                          (x.pm.Role == ProjectRole.ProjectAdmin ||
                           x.pm.Role == ProjectRole.Developer ||
                           (x.pm.Role == ProjectRole.AppUser &&
                            (!x.pa.Project!.OrganizationId.HasValue ||
                             dbContext.OrganizationAppAssignments.Any(assignment =>
                                 assignment.OrganizationId == x.pa.Project.OrganizationId &&
                                 assignment.AppId == appId &&
                                 assignment.UserId == userId &&
                                 assignment.IsActive &&
                                 dbContext.OrganizationMembers.Any(member =>
                                     member.OrganizationId == assignment.OrganizationId &&
                                     member.UserId == userId &&
                                     member.IsActive))))),
                cancellationToken);

    public Task<bool> CanExecuteAssignedOrganizationAppAsync(
        Guid appId,
        Guid userId,
        CancellationToken cancellationToken) => dbContext.OrganizationAppAssignments
            .AnyAsync(assignment =>
                assignment.AppId == appId &&
                assignment.UserId == userId &&
                assignment.IsActive &&
                dbContext.Organizations.Any(organization =>
                    organization.Id == assignment.OrganizationId &&
                    organization.IsActive) &&
                dbContext.OrganizationMembers.Any(member =>
                    member.OrganizationId == assignment.OrganizationId &&
                    member.UserId == userId &&
                    member.IsActive) &&
                dbContext.ProjectApps.Any(projectApp =>
                    projectApp.WebAppId == appId &&
                    projectApp.Project!.OrganizationId == assignment.OrganizationId &&
                    projectApp.Project.IsActive),
                cancellationToken);

    #endregion

    private bool IsTrustedInfrastructureAdministrator(Guid userId)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var currentUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return principal?.Identity?.IsAuthenticated == true
            && Guid.TryParse(currentUserId, out var authenticatedUserId)
            && authenticatedUserId == userId
            && (principal.IsInRole(UserRole.Administrator) || principal.IsInRole(UserRole.SystemAdministrator));
    }

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
    public IQueryable<WebApp> GetAccessibleAppsQuery()
    {
        return GetAccessibleAppsQuery(UserId);
    }

    public IQueryable<WebApp> GetAccessibleAppsQuery(Guid userId)
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
    public IQueryable<Flow> GetAccessibleFlowsQuery()
    {
        return GetAccessibleFlowsQuery(UserId);
    }

    public IQueryable<Flow> GetAccessibleFlowsQuery(Guid userId)
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
    public IQueryable<CustomStep> GetAccessibleCustomStepsQuery()
    {
        return GetAccessibleCustomStepsQuery(UserId);
    }

    public IQueryable<CustomStep> GetAccessibleCustomStepsQuery(Guid userId)
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
    public IQueryable<Runner> GetAccessibleRunnersQuery()
    {
        return GetAccessibleRunnersQuery(UserId);
    }

    public IQueryable<Runner> GetAccessibleRunnersQuery(Guid userId)
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

    /// <summary>
    /// Get IQueryable for apps filtered by project context.
    /// If projectId is null, returns only personal apps. Otherwise, returns only apps in that project.
    /// </summary>
    public IQueryable<WebApp> GetAppsQueryByContext(Guid? projectId)
    {
        return GetAppsQueryByContext(projectId, UserId);
    }

    public IQueryable<WebApp> GetAppsQueryByContext(Guid? projectId, Guid userId)
    {
        if (projectId == null)
        {
            // Personal mode - return only personal apps
            return dbContext.WebApps
                .Where(a => !a.ProjectId.HasValue && a.WebAppUsers.Any(u => u.UserId == userId));
        }
        else
        {
            // Project mode - return only apps in that project (if user has access)
            return dbContext.WebApps
                .Where(a => a.ProjectApps.Any(pa => pa.ProjectId == projectId && 
                                                     pa.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive)));
        }
    }

    /// <summary>
    /// Get IQueryable for flows filtered by project context.
    /// If projectId is null, returns only personal flows. Otherwise, returns only flows in that project.
    /// </summary>
    public IQueryable<Flow> GetFlowsQueryByContext(Guid? projectId)
    {
        return GetFlowsQueryByContext(projectId, UserId);
    }

    public IQueryable<Flow> GetFlowsQueryByContext(Guid? projectId, Guid userId)
    {
        if (projectId == null)
        {
            // Personal mode - return only personal flows
            return dbContext.Flows
                .Where(f => f.FlowUsers.Any(u => u.UserId == userId));
        }
        else
        {
            // Project mode - return only flows in that project (if user has access)
            var projectFlowIds = dbContext.ProjectFlows
                .Where(pcs => pcs.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
                .Select(pcs => pcs.FlowId);

            var projectFlows = dbContext.Flows
                .Where(cs => projectFlowIds.Contains(cs.Id));

            return projectFlows;
        }
    }

    /// <summary>
    /// Get IQueryable for custom steps filtered by project context.
    /// If projectId is null, returns only personal steps. Otherwise, returns only steps in that project.
    /// </summary>
    public IQueryable<CustomStep> GetCustomStepsQueryByContext(Guid? projectId)
    {
        return GetCustomStepsQueryByContext(projectId, UserId);
    }

    public IQueryable<CustomStep> GetCustomStepsQueryByContext(Guid? projectId, Guid userId)
    {
        if (projectId == null)
        {
            // Personal mode - return only personal steps
            return dbContext.CustomSteps
                .Where(cs => cs.CustomStepUsers.Any(u => u.UserId == userId));
        }
        else
        {
            // Project mode - return only steps in that project (if user has access)
            var projectStepsIds = dbContext.ProjectCustomSteps
                .Where(pcs => pcs.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
                .Select(pcs => pcs.CustomStepId);

            var projectSteps = dbContext.CustomSteps
                .Where(cs => projectStepsIds.Contains(cs.Id));

            return projectSteps;
        }
    }

    /// <summary>
    /// Get IQueryable for runners filtered by project context.
    /// If projectId is null, returns only personal runners. Otherwise, returns only runners in that project.
    /// </summary>
    public IQueryable<Runner> GetRunnersQueryByContext(Guid? projectId)
    {
        return GetRunnersQueryByContext(projectId, UserId);
    }

    public IQueryable<Runner> GetRunnersQueryByContext(Guid? projectId, Guid userId)
    {
        if (projectId == null)
        {
            // Personal mode - return only personal runners
            return dbContext.Runners
                .Where(r => r.RunnerUsers.Any(u => u.UserId == userId));
        }
        else
        {
            // Project mode - return only runners in that project (if user has access)
            var projectRunnerIds = dbContext.ProjectRunners
                .Where(pcs =>
                    pcs.ProjectId == projectId.Value &&
                    pcs.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
                .Select(pcs => pcs.RunnerId);

            var projectRunners = dbContext.Runners
                .Where(cs => projectRunnerIds.Contains(cs.Id));

            return projectRunners;
        }
    }

    /// <summary>
    /// Get IQueryable for API keys filtered by project context.
    /// If projectId is null, returns only personal API keys. Otherwise, returns only API keys in that project.
    /// </summary>
    public IQueryable<ApiKey> GetApiKeysQueryByContext(Guid? projectId)
    {
        return GetApiKeysQueryByContext(projectId, UserId);
    }

    public IQueryable<ApiKey> GetApiKeysQueryByContext(Guid? projectId, Guid userId)
    {
        if (projectId == null)
        {
            // Personal mode - return only personal API keys
            return dbContext.ApiKeys
                .Where(ak => ak.ApiKeyUsers.Any(u => u.UserId == userId));
        }
        else
        {
            // Project mode - return only API keys in that project (if user has access)
            return dbContext.ApiKeys
                .Where(ak => ak.ProjectApiKeys.Any(pak => pak.ProjectId == projectId && 
                                                           pak.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive)));
        }
    }

    /// <summary>
    /// Get IQueryable for AI API keys filtered by project context.
    /// If projectId is null, returns only personal AI API keys. Otherwise, returns only AI API keys in that project.
    /// </summary>
    public IQueryable<AiApiKey> GetAiApiKeysQueryByContext(Guid? projectId)
    {
        return GetAiApiKeysQueryByContext(projectId, UserId);
    }

    public IQueryable<AiApiKey> GetAiApiKeysQueryByContext(Guid? projectId, Guid userId)
    {
        if (projectId == null)
        {
            // Personal mode - return all active system-level AI API keys
            return dbContext.AiApiKeys.Where(ak => ak.IsActive);
        }
        else
        {
            // Project mode - return only AI API keys in that project (if user has access)
            return dbContext.AiApiKeys
                .Where(ak => ak.ProjectAiApiKeys.Any(pak => pak.ProjectId == projectId && 
                                                             pak.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive)) && 
                             ak.IsActive);
        }
    }

    #endregion

    #region Projects Authorization

    /// <summary>
    /// Check if user can access a project (read, view)
    /// User can access if they are an active member of the project
    /// </summary>
    public async Task<bool> CanAccessProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await CanAccessProjectAsync(projectId, UserId, cancellationToken);
    }

    public async Task<bool> CanAccessProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        var isMember = await dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);

        return isMember;
    }

    /// <summary>
    /// Check if user can update a project (modify settings)
    /// Only ProjectAdmin can update project settings
    /// </summary>
    public async Task<bool> CanUpdateProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await CanUpdateProjectAsync(projectId, UserId, cancellationToken);
    }

    public async Task<bool> CanUpdateProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);

        return membership != null && membership.Role == ProjectRole.ProjectAdmin;
    }

    /// <summary>
    /// Check if user can delete a project
    /// Only the project owner can delete (deactivate) a project
    /// </summary>
    public async Task<bool> CanDeleteProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await CanDeleteProjectAsync(projectId, UserId, cancellationToken);
    }

    public async Task<bool> CanDeleteProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        return project != null && project.OwnerId == userId;
    }

    /// <summary>
    /// Check if user has a specific role or higher in a project
    /// Role hierarchy: ProjectAdmin (0) > Developer (1) > AppUser (2) > Viewer (3)
    /// </summary>
    public async Task<bool> HasRoleInProjectAsync(Guid projectId, Guid userId, ProjectRole minimumRole, CancellationToken cancellationToken = default)
    {
        var membership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);

        if (membership == null)
        {
            return false;
        }

        // Role hierarchy: ProjectAdmin (0) > Developer (1) > AppUser (2) > Viewer (3)
        // Lower enum value = higher privilege
        return membership.Role <= minimumRole;
    }

    /// <summary>
    /// Get user's role in a project
    /// </summary>
    public async Task<ProjectRole?> GetUserRoleInProjectAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);

        return membership?.Role;
    }

    /// <summary>
    /// Get user's membership in a project
    /// </summary>
    public async Task<ProjectMember?> GetUserProjectMembershipAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ProjectMembers
            .Include(pm => pm.Project)
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);
    }

    #endregion

    #region API Keys Authorization

    /// <summary>
    /// Check if user can access an API key
    /// User can access only if they are the owner
    /// </summary>
    public async Task<bool> CanAccessApiKeyAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        return await CanAccessApiKeyAsync(apiKeyId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can access an API key
    /// User can access if:
    /// 1. They own it (through ApiKeyUser)
    /// 2. They are a member of a project that has access to it (through ProjectApiKey) with appropriate role
    /// </summary>
    public async Task<bool> CanAccessApiKeyAsync(Guid apiKeyId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check personal ownership
        var isOwner = await dbContext.ApiKeyUsers
            .AnyAsync(aku => aku.ApiKeyId == apiKeyId && aku.UserId == userId, cancellationToken);
        
        if (isOwner)
            return true;
        
        // Check project membership with appropriate role
        var hasProjectAccess = await dbContext.ProjectApiKeys
            .Where(pak => pak.ApiKeyId == apiKeyId)
            .Join(dbContext.ProjectMembers, 
                pak => pak.ProjectId, 
                pm => pm.ProjectId, 
                (pak, pm) => new { pak, pm })
            .AnyAsync(x => x.pm.UserId == userId && x.pm.IsActive, cancellationToken);
        
        return hasProjectAccess;
    }

    /// <summary>
    /// Check if user can update an API key
    /// User can update only if they are the owner
    /// </summary>
    public async Task<bool> CanUpdateApiKeyAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        return await CanUpdateApiKeyAsync(apiKeyId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can update an API key
    /// Only the owner can update
    /// </summary>
    public async Task<bool> CanUpdateApiKeyAsync(Guid apiKeyId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Only owner can update
        return await dbContext.ApiKeyUsers
            .AnyAsync(aku => aku.ApiKeyId == apiKeyId && aku.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// Check if user can delete an API key
    /// User can delete only if they are the owner
    /// </summary>
    public async Task<bool> CanDeleteApiKeyAsync(Guid apiKeyId, CancellationToken cancellationToken = default)
    {
        return await CanDeleteApiKeyAsync(apiKeyId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can delete an API key
    /// Only the owner can delete
    /// </summary>
    public async Task<bool> CanDeleteApiKeyAsync(Guid apiKeyId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Only owner can delete
        return await dbContext.ApiKeyUsers
            .AnyAsync(aku => aku.ApiKeyId == apiKeyId && aku.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// Get IQueryable for API keys that the user can access
    /// Includes both personally owned keys and keys from projects the user is a member of
    /// </summary>
    public IQueryable<ApiKey> GetAccessibleApiKeysQuery()
    {
        return GetAccessibleApiKeysQuery(UserId);
    }

    public IQueryable<ApiKey> GetAccessibleApiKeysQuery(Guid userId)
    {
        // Get API keys the user owns
        var ownedKeys = dbContext.ApiKeyUsers
            .Where(aku => aku.UserId == userId)
            .Select(aku => aku.ApiKeyId);
        
        // Get API keys from projects the user is a member of
        var projectKeys = dbContext.ProjectMembers
            .Where(pm => pm.UserId == userId && pm.IsActive)
            .Join(dbContext.ProjectApiKeys, 
                pm => pm.ProjectId, 
                pak => pak.ProjectId, 
                (pm, pak) => pak.ApiKeyId);
        
        // Combine both sets
        var accessibleKeyIds = ownedKeys.Union(projectKeys);
        
        return dbContext.ApiKeys.Where(ak => accessibleKeyIds.Contains(ak.Id));
    }

    #endregion

    #region AI API Keys Authorization

    /// <summary>
    /// Check if user can access an AI API key
    /// System-level keys are accessible to all users
    /// </summary>
    public async Task<bool> CanAccessAiApiKeyAsync(Guid aiApiKeyId, CancellationToken cancellationToken = default)
    {
        return await CanAccessAiApiKeyAsync(aiApiKeyId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can access an AI API key
    /// System-level keys are accessible to all users
    /// </summary>
    public async Task<bool> CanAccessAiApiKeyAsync(Guid aiApiKeyId, Guid userId, CancellationToken cancellationToken = default)
    {
        // System-level keys - just check if the key exists and is active
        return await dbContext.AiApiKeys
            .AnyAsync(ak => ak.Id == aiApiKeyId && ak.IsActive, cancellationToken);
    }

    /// <summary>
    /// Check if user can update an AI API key
    /// Only administrators can update system-level keys
    /// </summary>
    public async Task<bool> CanUpdateAiApiKeyAsync(Guid aiApiKeyId, CancellationToken cancellationToken = default)
    {
        return await CanUpdateAiApiKeyAsync(aiApiKeyId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can update an AI API key
    /// Only administrators can update system-level keys
    /// </summary>
    public async Task<bool> CanUpdateAiApiKeyAsync(Guid aiApiKeyId, Guid userId, CancellationToken cancellationToken = default)
    {
        // System-level keys - only administrators can update
        return await dbContext.UserRoles
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
            .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Administrators", cancellationToken);
    }

    /// <summary>
    /// Check if user can delete an AI API key
    /// Only administrators can delete system-level keys
    /// </summary>
    public async Task<bool> CanDeleteAiApiKeyAsync(Guid aiApiKeyId, CancellationToken cancellationToken = default)
    {
        return await CanDeleteAiApiKeyAsync(aiApiKeyId, UserId, cancellationToken);
    }

    /// <summary>
    /// Check if a specific user can delete an AI API key
    /// Only administrators can delete system-level keys
    /// </summary>
    public async Task<bool> CanDeleteAiApiKeyAsync(Guid aiApiKeyId, Guid userId, CancellationToken cancellationToken = default)
    {
        // System-level keys - only administrators can delete
        return await dbContext.UserRoles
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur, r })
            .AnyAsync(x => x.ur.UserId == userId && x.r.Name == "Administrators", cancellationToken);
    }

    /// <summary>
    /// Get IQueryable for AI API keys that the user can access
    /// System-level keys are accessible to all users
    /// </summary>
    public IQueryable<AiApiKey> GetAccessibleAiApiKeysQuery()
    {
        return GetAccessibleAiApiKeysQuery(UserId);
    }

    public IQueryable<AiApiKey> GetAccessibleAiApiKeysQuery(Guid userId)
    {
        // Return all active system-level AI API keys
        return dbContext.AiApiKeys.Where(ak => ak.IsActive);
    }

    #endregion
}
