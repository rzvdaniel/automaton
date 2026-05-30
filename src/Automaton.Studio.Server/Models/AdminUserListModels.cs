using Automaton.Studio.Server.Entities;

namespace Automaton.Studio.Server.Models;

public class UserListSummary
{
    public int TotalCount { get; set; }
    public int VerifiedCount { get; set; }
    public int ActiveCount { get; set; }
    public int JoinedTodayCount { get; set; }
    public int JoinedThisWeekCount { get; set; }
    public int JoinedThisMonthCount { get; set; }
}

public class ApplicationUserPageResult : PagedResult<ApplicationUser>
{
    public UserListSummary Summary { get; set; } = new();
}

public class AdminUserListResponse : PagedResult<UserDetails>
{
    public UserListSummary Summary { get; set; } = new();
}