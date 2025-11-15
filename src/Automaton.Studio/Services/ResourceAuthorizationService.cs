using Automaton.Studio.Enums;
using Serilog;

namespace Automaton.Studio.Services;

/// <summary>
/// UI-side service for resource access authorization.
/// Handles resource visibility and management controls based on project context and user roles.
/// In Personal Mode: users can create/manage all their own resources without restrictions.
/// In Project Mode: only ProjectAdmin and Developer roles can create/manage resources.
/// </summary>
public class ResourceAuthorizationService
{
    private readonly ProjectContextService _projectContextService;
    private readonly ILogger _logger;

    public ResourceAuthorizationService(ProjectContextService projectContextService)
    {
        _projectContextService = projectContextService;
        _logger = Log.ForContext<ResourceAuthorizationService>();
    }

    #region General Permission Checks

    /// <summary>
    /// Check if user is in personal mode (no project selected).
    /// In personal mode, users can create resources freely.
    /// </summary>
    public bool IsInPersonalMode()
    {
        return _projectContextService.CurrentProject == null;
    }

    /// <summary>
    /// Check if user is in project mode (a project is selected).
    /// </summary>
    public bool IsInProjectMode()
    {
        return _projectContextService.CurrentProject != null;
    }

    /// <summary>
    /// Get the current project role, if in project mode.
    /// Returns null if in personal mode.
    /// </summary>
    public ProjectRole? GetCurrentProjectRole()
    {
        return _projectContextService.CurrentProject?.CurrentUserRole;
    }

    /// <summary>
    /// Check if user has a specific role or higher in the current project.
    /// Role hierarchy: ProjectAdmin (0) > Developer (1) > AppUser (2) > Viewer (3)
    /// Returns true if in personal mode (unrestricted).
    /// </summary>
    public bool HasRoleOrHigher(ProjectRole minimumRole)
    {
        // Personal mode - no restrictions
        if (IsInPersonalMode())
        {
            return true;
        }

        var currentRole = GetCurrentProjectRole();
        if (currentRole == null)
        {
            return false;
        }

        // Lower enum value = higher privilege
        return currentRole.Value <= minimumRole;
    }

    /// <summary>
    /// Check if user is a project admin or project owner.
    /// Returns true if in personal mode.
    /// </summary>
    public bool IsProjectAdminOrOwner()
    {
        if (IsInPersonalMode())
        {
            return true;
        }

        var project = _projectContextService.CurrentProject;
        if (project == null)
        {
            return false;
        }

        return project.IsOwner || project.CurrentUserRole == ProjectRole.ProjectAdmin;
    }

    #endregion

    #region Resource Creation Permissions

    /// <summary>
    /// Check if user can create an app.
    /// In Personal Mode: always true
    /// In Project Mode: only ProjectAdmin and Developer
    /// </summary>
    public bool CanCreateApp()
    {
        if (IsInPersonalMode())
        {
            _logger.Debug("CanCreateApp: Personal mode - allowing creation");
            return true;
        }

        var canCreate = HasRoleOrHigher(ProjectRole.Developer);
        _logger.Debug("CanCreateApp: Project mode - Role: {Role}, CanCreate: {CanCreate}", 
            GetCurrentProjectRole(), canCreate);
        return canCreate;
    }

    /// <summary>
    /// Check if user can create a flow.
    /// In Personal Mode: always true
    /// In Project Mode: only ProjectAdmin and Developer
    /// </summary>
    public bool CanCreateFlow()
    {
        if (IsInPersonalMode())
        {
            _logger.Debug("CanCreateFlow: Personal mode - allowing creation");
            return true;
        }

        var canCreate = HasRoleOrHigher(ProjectRole.Developer);
        _logger.Debug("CanCreateFlow: Project mode - Role: {Role}, CanCreate: {CanCreate}", 
            GetCurrentProjectRole(), canCreate);
        return canCreate;
    }

    /// <summary>
    /// Check if user can create a custom step.
    /// In Personal Mode: always true
    /// In Project Mode: only ProjectAdmin and Developer
    /// </summary>
    public bool CanCreateCustomStep()
    {
        if (IsInPersonalMode())
        {
            _logger.Debug("CanCreateCustomStep: Personal mode - allowing creation");
            return true;
        }

        var canCreate = HasRoleOrHigher(ProjectRole.Developer);
        _logger.Debug("CanCreateCustomStep: Project mode - Role: {Role}, CanCreate: {CanCreate}", 
            GetCurrentProjectRole(), canCreate);
        return canCreate;
    }

    /// <summary>
    /// Check if user can create a runner.
    /// In Personal Mode: always true
    /// In Project Mode: only ProjectAdmin and Developer
    /// </summary>
    public bool CanCreateRunner()
    {
        if (IsInPersonalMode())
        {
            _logger.Debug("CanCreateRunner: Personal mode - allowing creation");
            return true;
        }

        var canCreate = HasRoleOrHigher(ProjectRole.Developer);
        _logger.Debug("CanCreateRunner: Project mode - Role: {Role}, CanCreate: {CanCreate}", 
            GetCurrentProjectRole(), canCreate);
        return canCreate;
    }

    /// <summary>
    /// Check if user can create an API key.
    /// API keys are personal and not project-scoped, so always allowed.
    /// </summary>
    public bool CanCreateApiKey()
    {
        // API keys are always personal, regardless of project context
        return true;
    }

    /// <summary>
    /// Check if user can create an AI API key.
    /// AI API keys are personal and not project-scoped, so always allowed.
    /// </summary>
    public bool CanCreateAiApiKey()
    {
        // AI API keys are always personal, regardless of project context
        return true;
    }

    /// <summary>
    /// Check if user can create a project.
    /// Projects can always be created (user becomes owner).
    /// </summary>
    public bool CanCreateProject()
    {
        return true;
    }

    #endregion

    #region Resource Management Permissions

    /// <summary>
    /// Check if user can update/delete resources in the current context.
    /// In Personal Mode: always true for owned resources
    /// In Project Mode: only ProjectAdmin and Developer
    /// Note: This is a general check. Specific resource ownership is validated server-side.
    /// </summary>
    public bool CanManageResources()
    {
        if (IsInPersonalMode())
        {
            return true;
        }

        return HasRoleOrHigher(ProjectRole.Developer);
    }

    /// <summary>
    /// Check if user can execute apps/flows.
    /// In Personal Mode: always true
    /// In Project Mode: ProjectAdmin, Developer, and AppUser (not Viewer)
    /// </summary>
    public bool CanExecuteResources()
    {
        if (IsInPersonalMode())
        {
            return true;
        }

        return HasRoleOrHigher(ProjectRole.AppUser);
    }

    /// <summary>
    /// Check if user can view resources.
    /// In Personal Mode: always true for owned resources
    /// In Project Mode: all roles can view (including Viewer)
    /// </summary>
    public bool CanViewResources()
    {
        // Everyone can view resources they have access to
        return true;
    }

    /// <summary>
    /// Check if user can manage project settings.
    /// Only project admins and owners can manage project settings.
    /// </summary>
    public bool CanManageProjectSettings()
    {
        if (IsInPersonalMode())
        {
            return false; // No project to manage in personal mode
        }

        return IsProjectAdminOrOwner();
    }

    /// <summary>
    /// Check if user can manage project members.
    /// Only project admins and owners can manage members.
    /// </summary>
    public bool CanManageProjectMembers()
    {
        if (IsInPersonalMode())
        {
            return false; // No project to manage in personal mode
        }

        return IsProjectAdminOrOwner();
    }

    /// <summary>
    /// Check if user can delete the current project.
    /// Only the project owner can delete a project.
    /// </summary>
    public bool CanDeleteProject()
    {
        if (IsInPersonalMode())
        {
            return false;
        }

        var project = _projectContextService.CurrentProject;
        return project?.IsOwner ?? false;
    }

    #endregion

    #region UI Helper Methods

    /// <summary>
    /// Get a user-friendly message explaining why resource creation is not allowed.
    /// </summary>
    public string GetCreationDeniedMessage(string resourceType)
    {
        if (IsInPersonalMode())
        {
            return $"You can create {resourceType} in personal mode.";
        }

        var role = GetCurrentProjectRole();
        var projectName = _projectContextService.CurrentProject?.DisplayName ?? "this project";

        return role switch
        {
            ProjectRole.Viewer => $"Viewers cannot create {resourceType} in {projectName}. Contact a project admin to upgrade your role to Developer or higher.",
            ProjectRole.AppUser => $"App Users cannot create {resourceType} in {projectName}. Contact a project admin to upgrade your role to Developer or higher.",
            _ => $"You don't have permission to create {resourceType} in {projectName}."
        };
    }

    /// <summary>
    /// Get the current context description for logging/display.
    /// </summary>
    public string GetCurrentContextDescription()
    {
        if (IsInPersonalMode())
        {
            return "Personal Workspace";
        }

        var project = _projectContextService.CurrentProject;
        return $"Project: {project?.DisplayName} (Role: {project?.CurrentUserRole})";
    }

    #endregion
}
