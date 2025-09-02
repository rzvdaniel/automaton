using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services
{
    public class DashboardStatsService
    {
        private readonly ApplicationDbContext dbContext;
        private readonly RunnerService runnerService;
        private readonly Guid userId;
        private readonly Serilog.ILogger logger;

        public DashboardStatsService(
            ApplicationDbContext dbContext,
            RunnerService runnerService,
            UserContextService userContextService)
        {
            this.dbContext = dbContext;
            this.runnerService = runnerService;
            this.userId = userContextService.GetUserId();
            this.logger = Log.ForContext<DashboardStatsService>();
        }

        public async Task<Models.DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Get flow count efficiently
                var flowStats = await dbContext.Flows
                    .Where(f => f.FlowUsers.Any(u => u.UserId == userId))
                    .GroupBy(f => 1)
                    .Select(g => new { Count = g.Count() })
                    .FirstOrDefaultAsync(cancellationToken);

                // Use RunnerService to get accurate runner status information
                // This leverages the existing GetRunnersWithUpdatedStatus logic that pings runners
                var runners = await runnerService.List(cancellationToken);
                var totalRunners = runners.Count();
                var onlineRunners = runners.Count(r => r.Status == RunnerStatus.Online);

                // Recent executions for success rate calculation
                var cutoffDate = DateTime.UtcNow.AddDays(-7); // Last 7 days
                var executionStats = await dbContext.FlowExecutions
                    .Where(e => e.FlowExecutionUsers.Any(u => u.UserId == userId) &&
                               e.Started >= cutoffDate)
                    .GroupBy(e => 1)
                    .Select(g => new {
                        TotalCount = g.Count(),
                        SuccessCount = g.Count(e => e.Status == WorkflowStatus.Success.ToString())
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                var totalFlows = flowStats?.Count ?? 0;
                var totalExecutions = executionStats?.TotalCount ?? 0;
                var successfulExecutions = executionStats?.SuccessCount ?? 0;
                
                var successRate = totalExecutions > 0 ? (double)successfulExecutions / totalExecutions * 100 : 0;

                return new Models.DashboardStats
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
                logger.Error(ex, "Failed to get dashboard stats for user {UserId}", userId);
                return new Models.DashboardStats();
            }
        }
    }
}