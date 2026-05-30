namespace Automaton.Studio.Server.Models;

public class AdminDashboardSummaryResponse
{
    public int TotalUsers { get; set; }
    public int NewRegistrations { get; set; }
    public int PublishedApps { get; set; }
    public int ActiveRunners { get; set; }
    public int ExecutionsToday { get; set; }
    public int FailedExecutions { get; set; }
}
