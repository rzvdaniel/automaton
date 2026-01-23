namespace Automaton.Studio.Server.Models
{
    public class DashboardStats
    {
        // Legacy fields (kept for backwards compatibility)
        public int TotalFlows { get; set; }
        public int TotalRunners { get; set; }
        public int OnlineRunners { get; set; }
        public double SuccessRate { get; set; }
        public int RecentExecutions { get; set; }

        // Additional metrics for enhanced dashboard
        public int TotalApps { get; set; }
        public int TotalCustomSteps { get; set; }
        public int ActiveSchedules { get; set; }
        public int ExecutionsToday { get; set; }
        public int ExecutionsThisWeek { get; set; }
        public int ExecutionsThisMonth { get; set; }
        public List<DailyExecutionTrend> ExecutionTrends { get; set; } = new();
        public List<FlowHealthMetric> TopFlows { get; set; } = new();

        // Apps-focused metrics
        public int PublishedApps { get; set; }
        public int TotalAppViews { get; set; }
        public List<RecentAppMetric> RecentApps { get; set; } = new();
        public List<PublishedAppMetric> PublishedAppsList { get; set; } = new();
    }

    public class RecentAppMetric
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public bool IsPublished { get; set; }
        public string? PublicUrl { get; set; }
        public int ViewCount { get; set; }
        public DateTime Updated { get; set; }
    }

    public class PublishedAppMetric
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string PublicUrl { get; set; }
        public int ViewCount { get; set; }
        public DateTime? PublishedAt { get; set; }
    }
    
    public class DailyExecutionTrend
    {
        public DateTime Date { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int TotalCount { get; set; }
    }
    
    public class FlowHealthMetric
    {
        public Guid FlowId { get; set; }
        public string FlowName { get; set; }
        public int ExecutionCount { get; set; }
        public double SuccessRate { get; set; }
        public DateTime LastExecuted { get; set; }
    }
}