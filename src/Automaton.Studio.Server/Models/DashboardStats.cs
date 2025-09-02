namespace Automaton.Studio.Server.Models
{
    public class DashboardStats
    {
        public int TotalFlows { get; set; }
        public int TotalRunners { get; set; }
        public int OnlineRunners { get; set; }
        public double SuccessRate { get; set; }
        public int RecentExecutions { get; set; }
    }
}