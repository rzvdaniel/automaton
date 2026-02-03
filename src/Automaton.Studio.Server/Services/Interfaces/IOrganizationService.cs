using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;

namespace Automaton.Studio.Server.Services.Interfaces;

/// <summary>
/// Service interface for managing organizations and their members.
/// </summary>
public interface IOrganizationService
{
    // =====================================================
    // Organization CRUD
    // =====================================================
    
    /// <summary>
    /// Get all organizations where the current user is a member
    /// </summary>
    Task<IEnumerable<OrganizationListItem>> ListUserOrganizationsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get detailed organization information
    /// </summary>
    Task<OrganizationDto?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a new organization
    /// </summary>
    Task<OrganizationDto> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update an existing organization
    /// </summary>
    Task<OrganizationDto?> UpdateOrganizationAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete an organization (soft delete)
    /// </summary>
    Task<bool> DeleteOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
    
    // =====================================================
    // Membership Management
    // =====================================================
    
    /// <summary>
    /// Get all members of an organization
    /// </summary>
    Task<IEnumerable<OrganizationMemberDto>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the current user's role in an organization
    /// </summary>
    Task<OrganizationRole?> GetUserRoleAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invite a user to join the organization
    /// </summary>
    Task<OrganizationInvitation> InviteMemberAsync(Guid organizationId, InviteMemberRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Accept an invitation to join an organization
    /// </summary>
    Task<bool> AcceptInvitationAsync(string token, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all pending organization invitations for the current user
    /// </summary>
    Task<IEnumerable<PendingInvitationDto>> GetPendingInvitationsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Decline an organization invitation
    /// </summary>
    Task<bool> DeclineInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Remove a member from an organization
    /// </summary>
    Task<bool> RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    
    // =====================================================
    // Projects
    // =====================================================
    
    /// <summary>
    /// Get all projects in the organization
    /// </summary>
    Task<IEnumerable<ProjectDto>> GetOrganizationProjectsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Link an existing project to the organization
    /// </summary>
    Task<bool> LinkProjectToOrganizationAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unlink a project from the organization (make it personal again)
    /// </summary>
    Task<bool> UnlinkProjectFromOrganizationAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken = default);
    
    // =====================================================
    // App Assignment
    // =====================================================
    
    /// <summary>
    /// Get all apps from projects in the organization
    /// </summary>
    Task<IEnumerable<WebApp>> GetOrganizationAppsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Assign an app to a user within an organization
    /// </summary>
    Task<OrganizationAppAssignment> AssignAppToUserAsync(Guid organizationId, Guid appId, Guid userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unassign an app from a user
    /// </summary>
    Task<bool> UnassignAppFromUserAsync(Guid organizationId, Guid appId, Guid userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all apps assigned to a user in an organization
    /// </summary>
    Task<IEnumerable<WebApp>> GetAssignedAppsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all users that have access to a specific app in an organization
    /// </summary>
    Task<IEnumerable<ApplicationUser>> GetAppAssignedUsersAsync(Guid organizationId, Guid appId, CancellationToken cancellationToken = default);
}
