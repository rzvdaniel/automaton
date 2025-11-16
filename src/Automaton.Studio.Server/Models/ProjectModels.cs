using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Request model for creating a new project
/// </summary>
public class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Request model for updating a project
/// </summary>
public class UpdateProjectRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Response model for project details
/// </summary>
public class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int AppCount { get; set; }
    public int FlowCount { get; set; }
    public ProjectRole? CurrentUserRole { get; set; }
}

/// <summary>
/// Lightweight project list item
/// </summary>
public class ProjectListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProjectRole Role { get; set; }
    public int MemberCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request model for adding a member to a project
/// </summary>
public class AddProjectMemberRequest
{
    public string Email { get; set; } = string.Empty;
    public ProjectRole Role { get; set; }
    public bool CreateAccountIfNotExists { get; set; } = true;
}

/// <summary>
/// Request model for updating a member's role
/// </summary>
public class UpdateProjectMemberRequest
{
    public ProjectRole Role { get; set; }
}

/// <summary>
/// Response model for project member details
/// </summary>
public class ProjectMemberDto
{
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ProjectRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public Guid? InvitedBy { get; set; }
    public string? InvitedByName { get; set; }
    public bool IsActive { get; set; }
    public bool IsOwner { get; set; }
}

/// <summary>
/// Request model for adding a resource (app/flow/runner/step) to a project
/// </summary>
public class AddProjectResourceRequest
{
    public Guid ResourceId { get; set; }
}

/// <summary>
/// Response model for project app
/// </summary>
public class ProjectAppDto
{
    public Guid ProjectId { get; set; }
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public Guid? AddedBy { get; set; }
    public string AddedByName { get; set; } = string.Empty;
}

/// <summary>
/// Response model for project flow
/// </summary>
public class ProjectFlowDto
{
    public Guid ProjectId { get; set; }
    public Guid FlowId { get; set; }
    public string FlowName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public Guid? AddedBy { get; set; }
    public string AddedByName { get; set; } = string.Empty;
}

/// <summary>
/// Response model for project runner
/// </summary>
public class ProjectRunnerDto
{
    public Guid ProjectId { get; set; }
    public Guid RunnerId { get; set; }
    public string RunnerName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public Guid? AddedBy { get; set; }
    public string AddedByName { get; set; } = string.Empty;
}

/// <summary>
/// Response model for project custom step
/// </summary>
public class ProjectCustomStepDto
{
    public Guid ProjectId { get; set; }
    public Guid StepId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public Guid? AddedBy { get; set; }
    public string AddedByName { get; set; } = string.Empty;
}

/// <summary>
/// Response model for project API key
/// </summary>
public class ProjectApiKeyDto
{
    public Guid ProjectId { get; set; }
    public Guid ApiKeyId { get; set; }
    public string ApiKeyName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public Guid? AddedBy { get; set; }
    public string AddedByName { get; set; } = string.Empty;
}

/// <summary>
/// Response model for project AI API key
/// </summary>
public class ProjectAiApiKeyDto
{
    public Guid ProjectId { get; set; }
    public Guid AiApiKeyId { get; set; }
    public string AiApiKeyName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public Guid? AddedBy { get; set; }
    public string AddedByName { get; set; } = string.Empty;
}
