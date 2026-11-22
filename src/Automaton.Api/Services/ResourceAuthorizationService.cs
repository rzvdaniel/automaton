using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;
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

    private Guid UserId => userContextService.GetUserId();

    public ResourceAuthorizationService(
        ApplicationDbContext dbContext,
        UserContextService userContextService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
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

    /// <summary>Checks app runtime use, including explicit organization assignment, without granting app administration.</summary>
    public async Task<bool> CanUseAppRuntimeAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
        => await CanAccessAppAsync(appId, userId, cancellationToken) ||
           await CanExecuteAssignedOrganizationAppAsync(appId, userId, cancellationToken) ||
           await IsSharedWithAsync(appId, userId, cancellationToken) ||
           await IsAdministratorAsync(userId, cancellationToken);

    /// <summary>True when the owner shared the app with this person, with a team they belong to, or with everyone on the server.</summary>
    public async Task<bool> IsSharedWithAsync(Guid appId, Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.AppShares.AnyAsync(share => share.WebAppId == appId && share.UserId == userId, cancellationToken) ||
           await dbContext.WebApps.AnyAsync(app => app.Id == appId && app.DeletionClaimId == null &&
               (app.SharedWithServer ||
                (app.SharedWithProjectId != null && dbContext.ProjectMembers.Any(member =>
                    member.ProjectId == app.SharedWithProjectId && member.UserId == userId && member.IsActive &&
                    member.Project!.IsActive))), cancellationToken);

    public async Task<bool> AppExistsAsync(Guid appId, CancellationToken cancellationToken = default)
        => await dbContext.WebApps.AnyAsync(app => app.Id == appId && app.DeletionClaimId == null, cancellationToken);

    /// <summary>True only for a system administrator, the role that may change server settings.</summary>
    public async Task<bool> IsSystemAdministratorAsync(Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.UserRoles.AnyAsync(membership => membership.UserId == userId &&
            dbContext.Roles.Any(role => role.Id == membership.RoleId && role.Name == UserRole.SystemAdministrator), cancellationToken);

    /// <summary>True for administrators and system administrators, who may open every app on the server.</summary>
    public async Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.UserRoles.AnyAsync(membership => membership.UserId == userId &&
            dbContext.Roles.Any(role => role.Id == membership.RoleId &&
                (role.Name == UserRole.Administrator || role.Name == UserRole.SystemAdministrator)), cancellationToken);

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
        if (Automaton.Core.Models.InfrastructureAppTrust.IsTrustedApp(appId))
            return false;

        // Check personal ownership
        var hasPersonalAccess = await dbContext.WebAppUsers
            .AnyAsync(u => u.WebAppId == appId && u.UserId == userId, cancellationToken);

        if (!hasPersonalAccess)
        {
            return false;
        }

        // Check if app is part of any active project; links left by a deleted project do not block.
        var isPartOfProject = await dbContext.ProjectApps
            .AnyAsync(pa => pa.WebAppId == appId && pa.Project!.IsActive, cancellationToken);

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

        // Check project membership; a deleted project shares nothing (its links are kept for reactivation).
        var hasProjectAccess = await dbContext.ProjectRunners
            .Where(pr => pr.RunnerId == runnerId && pr.Project != null && pr.Project.IsActive)
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

    /// <remarks>
    /// Only the Runner's owner may change it. A Runner can serve several projects, so letting a
    /// project's Developers or Project Admins change it would affect projects they are not part of.
    /// Projects use a Runner; they decide only whether it is attached.
    /// </remarks>
    public async Task<bool> CanUpdateRunnerAsync(Guid runnerId, Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.RunnerUsers.AnyAsync(u => u.RunnerId == runnerId && u.UserId == userId, cancellationToken);

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
        var hasPersonalAccess = await HasPersonalRunnerAccessAsync(runnerId, userId, cancellationToken);

        if (!hasPersonalAccess)
        {
            return false;
        }

        // Check if runner is part of any active project; links of deleted projects do not count.
        var isPartOfProject = await dbContext.ProjectRunners
            .AnyAsync(pr => pr.RunnerId == runnerId && pr.Project != null && pr.Project.IsActive, cancellationToken);

        return !isPartOfProject;
    }

    public Task<bool> HasPersonalRunnerAccessAsync(Guid runnerId, CancellationToken cancellationToken = default)
        => HasPersonalRunnerAccessAsync(runnerId, UserId, cancellationToken);

    private Task<bool> HasPersonalRunnerAccessAsync(Guid runnerId, Guid userId, CancellationToken cancellationToken)
        => dbContext.RunnerUsers.AnyAsync(u => u.RunnerId == runnerId && u.UserId == userId, cancellationToken);

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
            .Where(pa => pa.Project != null && pa.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
            .Select(pa => pa.WebAppId);

        var projectApps = dbContext.WebApps
            .Where(a => projectAppIds.Contains(a.Id));

        return personalApps.Union(projectApps);
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
            .Where(pr => pr.Project != null && pr.Project.IsActive && pr.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
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

    /// <summary>
    /// Apps for the Apps list. In a project this is the project's apps. Without one it is the person's own apps plus
    /// project apps they may update (their own, or as a project Admin or Developer), so they can manage those from the list.
    /// </summary>
    public IQueryable<WebApp> GetListedAppsQuery(Guid? projectId)
    {
        if (projectId != null) return GetAppsQueryByContext(projectId, UserId);
        var userId = UserId;
        return dbContext.WebApps.Where(a =>
            a.WebAppUsers.Any(u => u.UserId == userId) ||
            a.ProjectApps.Any(pa => pa.Project != null && pa.Project.Members.Any(pm =>
                pm.UserId == userId && pm.IsActive &&
                (pm.Role == ProjectRole.ProjectAdmin || pm.Role == ProjectRole.Developer))));
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
                                                     pa.Project != null && pa.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive)));
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
                    pcs.Project != null && pcs.Project.IsActive && pcs.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive))
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
                .Where(ak => ak.ProjectApiKeys.Any(pak => pak.ProjectId == projectId && pak.Project != null &&
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
                .Where(ak => ak.ProjectAiApiKeys.Any(pak => pak.ProjectId == projectId && pak.Project != null &&
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
