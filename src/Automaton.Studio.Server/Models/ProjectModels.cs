using Automaton.Core.Enums;

namespace Automaton.Studio.Server.Models;

public class ProjectModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid AdminUserId { get; set; }
    public string? AdminUserName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int MemberCount { get; set; }
}

public class ProjectCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ProjectUpdateRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ProjectMemberModel
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public ProjectRole Role { get; set; }
    public DateTime AddedAt { get; set; }
}

public class AddProjectMemberRequest
{
    public Guid UserId { get; set; }
    public ProjectRole Role { get; set; }
}

public class UpdateProjectMemberRequest
{
    public ProjectRole Role { get; set; }
}
