using Automaton.Studio.Models;
using Automaton.Core.Enums;

namespace Automaton.Studio.Services;

public class DashboardService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly FlowExecutionsService flowExecutionsService;
    private readonly FlowsService flowsService;
    private readonly RunnerService runnerService;
    private readonly RecentActivityService recentActivityService;
    private readonly ProjectContextService projectContextService;
    private readonly ILogger logger;

    public DashboardService(
        HttpClient httpClient,
        ConfigurationService configService,
        FlowExecutionsService flowExecutionsService,
        FlowsService flowsService,
        RunnerService runnerService,
        RecentActivityService recentActivityService,
        ProjectContextService projectContextService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.flowExecutionsService = flowExecutionsService;
        this.flowsService = flowsService;
        this.runnerService = runnerService;
        this.recentActivityService = recentActivityService;
        this.projectContextService = projectContextService;
        this.logger = Log.ForContext<DashboardService>();
    }

    public async Task<IEnumerable<RecentActivity>> GetRecentActivityAsync(int count = 10)
    {
        try
        {
            // Use the dedicated recent activity service
            var recentActivities = await recentActivityService.GetRecentActivitiesAsync(count);
            return recentActivities;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load recent activity");
            return new List<RecentActivity>();
        }
    }

    public async Task<IEnumerable<FlowExecution>> GetRecentExecutionsAsync(int count = 10)
    {
        try
        {
            // Try to get recent executions from a dedicated endpoint first
            var result = await httpClient.GetAsync($"{configService.FlowExecutionUrl}/recent/{count}");
            
            if (result.IsSuccessStatusCode)
            {
                var recentExecutions = await result.Content.ReadAsAsync<IEnumerable<FlowExecution>>();
                return recentExecutions;
            }
            
            // Fallback: Get recent executions using existing pagination endpoint
            // This gets the first page of results and takes the requested count
            var fallbackResult = await flowExecutionsService.GetFlowExecutionResult(Guid.Empty, 0, count);
            return fallbackResult.FlowExecutions.Take(count);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load recent executions");
            return new List<FlowExecution>();
        }
    }

    public async Task<IEnumerable<FlowInfo>> GetRecentFlowsAsync(int count = 5)
    {
        try
        {
            var projectId = projectContextService.GetCurrentProjectId();
            var flows = await flowsService.List(projectId);
            return flows.OrderByDescending(f => f.Updated).Take(count);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load recent flows");
            return new List<FlowInfo>();
        }
    }

    public async Task<IEnumerable<FlowInfo>> GetAllRecentFlowsAsync(int count = 20)
    {
        try
        {
            var projectId = projectContextService.GetCurrentProjectId();
            var flows = await flowsService.List(projectId);
            return flows.OrderByDescending(f => f.Updated).Take(count);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load all recent flows");
            return new List<FlowInfo>();
        }
    }

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        try
        {
            // Use the optimized server-side stats endpoint
            var response = await httpClient.GetAsync($"{configService.BaseUrl}/api/dashboard/stats");
            
            if (response.IsSuccessStatusCode)
            {
                var stats = await response.Content.ReadAsAsync<DashboardStats>();
                return stats ?? new DashboardStats();
            }

            logger.Warning("Failed to fetch dashboard stats. Status: {StatusCode}", response.StatusCode);
            return new DashboardStats();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load dashboard stats");
            return new DashboardStats();
        }
    }
}

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