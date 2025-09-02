using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services
{
    public class DashboardStatsService
    {
        private readonly ApplicationDbContext dbContext;
        private readonly Guid userId;
        private readonly Serilog.ILogger logger;

        public DashboardStatsService(
            ApplicationDbContext dbContext,
            UserContextService userContextService)
        {
            this.dbContext = dbContext;
            this.userId = userContextService.GetUserId();
            this.logger = Log.ForContext<DashboardStatsService>();
        }

        public async Task<Models.DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Combine queries for better performance and avoid multiple round trips
                var flowStats = await dbContext.Flows
                    .Where(f => f.FlowUsers.Any(u => u.UserId == userId))
                    .GroupBy(f => 1)
                    .Select(g => new { Count = g.Count() })
                    .FirstOrDefaultAsync(cancellationToken);

                var runnerStats = await dbContext.Runners
                    .Where(r => r.RunnerUsers.Any(u => u.UserId == userId))
                    .GroupBy(r => 1)
                    .Select(g => new { 
                        TotalCount = g.Count(),
                        OnlineCount = g.Count(r => !string.IsNullOrEmpty(r.ConnectionId))
                    })
                    .FirstOrDefaultAsync(cancellationToken);

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
                var totalRunners = runnerStats?.TotalCount ?? 0;
                var onlineRunners = runnerStats?.OnlineCount ?? 0;
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