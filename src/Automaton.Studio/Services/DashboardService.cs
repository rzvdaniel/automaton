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
    private readonly ILogger logger;

    public DashboardService(
        HttpClient httpClient,
        ConfigurationService configService,
        FlowExecutionsService flowExecutionsService,
        FlowsService flowsService,
        RunnerService runnerService,
        RecentActivityService recentActivityService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.flowExecutionsService = flowExecutionsService;
        this.flowsService = flowsService;
        this.runnerService = runnerService;
        this.recentActivityService = recentActivityService;
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
            var flows = await flowsService.List();
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
            var flows = await flowsService.List();
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
            var flows = await flowsService.List();
            var runners = await runnerService.List();
            
            // Get recent executions to calculate success rate
            var recentExecutions = await GetRecentExecutionsAsync(100);
            
            var totalFlows = flows.Count();
            var totalRunners = runners.Count();
            var onlineRunners = runners.Count(r => r.Status == RunnerStatus.Online);
            
            var successfulExecutions = recentExecutions.Count(e => e.Status == WorkflowStatus.Success);
            var totalExecutions = recentExecutions.Count();
            var successRate = totalExecutions > 0 ? (double)successfulExecutions / totalExecutions * 100 : 0;

            return new DashboardStats
            {
                TotalFlows = totalFlows,
                TotalRunners = totalRunners,
                OnlineRunners = onlineRunners,
                SuccessRate = Math.Round(successRate, 1),
                RecentExecutions = totalExecutions
            };
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
}