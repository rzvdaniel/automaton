using Automaton.Studio.Enums;

namespace Automaton.Studio.Models;

// Request Models
public class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateProjectRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}

public class AddProjectMemberRequest
{
    public string Email { get; set; } = string.Empty;
    public ProjectRole Role { get; set; }
    public bool CreateAccountIfNotExists { get; set; } = true;
}

public class UpdateProjectMemberRequest
{
    public ProjectRole Role { get; set; }
}

// Response Models
public class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    public int MemberCount { get; set; }
    public int AppCount { get; set; }
    public int FlowCount { get; set; }
    public int RunnerCount { get; set; }
    public int CustomStepCount { get; set; }
    public ProjectRole? CurrentUserRole { get; set; }
    public bool IsOwner { get; set; }
}

public class ProjectListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public int AppCount { get; set; }
    public int FlowCount { get; set; }
    public ProjectRole? CurrentUserRole { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOwner { get; set; }
}

public class ProjectMemberDto
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public ProjectRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
    public string? InvitedBy { get; set; }
    public bool IsActive { get; set; }
    public bool IsOwner { get; set; }
}

public class ProjectMembershipDto
{
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public ProjectRole Role { get; set; }
    public bool IsMember { get; set; }
}

// Resource DTOs
public class ProjectAppDto
{
    public Guid ProjectId { get; set; }
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public string? AddedByUserName { get; set; }
}

public class ProjectFlowDto
{
    public Guid ProjectId { get; set; }
    public Guid FlowId { get; set; }
    public string FlowName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public string? AddedByUserName { get; set; }
}

public class ProjectRunnerDto
{
    public Guid ProjectId { get; set; }
    public Guid RunnerId { get; set; }
    public string RunnerName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public string? AddedByUserName { get; set; }
}

public class ProjectCustomStepDto
{
    public Guid ProjectId { get; set; }
    public Guid StepId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public string? AddedByUserName { get; set; }
}
