using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Models;

// =====================================================
// Organization Models
// =====================================================

/// <summary>
/// Request model for creating a new organization
/// </summary>
public class CreateOrganizationRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Slug { get; set; }
    public bool AllowPublicSignup { get; set; } = false;
    public string? RequireEmailDomain { get; set; }
}

/// <summary>
/// Request model for updating an organization
/// </summary>
public class UpdateOrganizationRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AllowPublicSignup { get; set; }
    public string? RequireEmailDomain { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
}

/// <summary>
/// Response model for organization details
/// </summary>
public class OrganizationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool AllowPublicSignup { get; set; }
    public string? RequireEmailDomain { get; set; }
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int ProjectCount { get; set; }
    public OrganizationRole? CurrentUserRole { get; set; }
}

/// <summary>
/// Lightweight organization list item
/// </summary>
public class OrganizationListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Slug { get; set; }
    public string? LogoUrl { get; set; }
    public OrganizationRole Role { get; set; }
    public int MemberCount { get; set; }
    public int ProjectCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// =====================================================
// Organization Member Models
// =====================================================

/// <summary>
/// Request model for inviting a member to an organization
/// </summary>
public class InviteMemberRequest
{
    public string Email { get; set; } = string.Empty;
    public OrganizationRole Role { get; set; }
}

/// <summary>
/// Request model for accepting an organization invitation
/// </summary>
public class AcceptInvitationRequest
{
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Request model for updating a member's role
/// </summary>
public class UpdateOrganizationMemberRequest
{
    public OrganizationRole Role { get; set; }
}

/// <summary>
/// Response model for organization member details
/// </summary>
public class OrganizationMemberDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public OrganizationRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public Guid? InvitedBy { get; set; }
    public string? InvitedByName { get; set; }
    public bool IsActive { get; set; }
    public bool IsOwner { get; set; }
}

// =====================================================
// App Assignment Models
// =====================================================

/// <summary>
/// Request model for assigning an app to a user
/// </summary>
public class AssignAppRequest
{
    public Guid UserId { get; set; }
}

/// <summary>
/// Response model for app assignment details
/// </summary>
public class OrganizationAppAssignmentDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public Guid? AssignedBy { get; set; }
    public string? AssignedByName { get; set; }
    public DateTime AssignedAt { get; set; }
}
/// <summary>
/// Response model for pending organization invitations
/// </summary>
public class PendingInvitationDto
{
    public Guid InvitationId { get; set; }
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string OrganizationSlug { get; set; } = string.Empty;
    public OrganizationRole Role { get; set; }
    public DateTime InvitedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Token { get; set; } = string.Empty;
}