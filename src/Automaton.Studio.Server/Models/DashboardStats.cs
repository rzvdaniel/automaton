namespace Automaton.Studio.Server.Models
{
    public class DashboardStats
    {
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