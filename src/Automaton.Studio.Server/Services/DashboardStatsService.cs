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

                // Get additional metrics
                var totalApps = await dbContext.WebApps
                    .Where(a => a.WebAppUsers.Any(u => u.UserId == userId))
                    .CountAsync(cancellationToken);

                // Apps-focused metrics
                var publishedApps = await dbContext.WebApps
                    .Where(a => a.WebAppUsers.Any(u => u.UserId == userId) && a.IsPublished)
                    .CountAsync(cancellationToken);

                var totalAppViews = await dbContext.WebApps
                    .Where(a => a.WebAppUsers.Any(u => u.UserId == userId))
                    .SumAsync(a => a.ViewCount, cancellationToken);

                var recentApps = await dbContext.WebApps
                    .Where(a => a.WebAppUsers.Any(u => u.UserId == userId))
                    .OrderByDescending(a => a.Updated)
                    .Take(5)
                    .Select(a => new Models.RecentAppMetric
                    {
                        Id = a.Id,
                        Name = a.Name,
                        DisplayName = a.DisplayName,
                        IsPublished = a.IsPublished,
                        PublicUrl = a.PublicUrl,
                        ViewCount = a.ViewCount,
                        Updated = a.Updated
                    })
                    .ToListAsync(cancellationToken);

                var publishedAppsList = await dbContext.WebApps
                    .Where(a => a.WebAppUsers.Any(u => u.UserId == userId) && a.IsPublished)
                    .OrderByDescending(a => a.ViewCount)
                    .Take(5)
                    .Select(a => new Models.PublishedAppMetric
                    {
                        Id = a.Id,
                        Name = a.Name,
                        DisplayName = a.DisplayName,
                        PublicUrl = a.PublicUrl!,
                        ViewCount = a.ViewCount,
                        PublishedAt = a.PublishedAt
                    })
                    .ToListAsync(cancellationToken);

                var totalCustomSteps = await dbContext.CustomSteps
                    .Where(cs => cs.CustomStepUsers.Any(u => u.UserId == userId))
                    .CountAsync(cancellationToken);
                
                var activeSchedules = await dbContext.Schedules
                    .Where(fs => fs.ScheduleUsers.Any(u => u.UserId == userId))
                    .CountAsync(cancellationToken);
                
                // Time-based execution counts
                var today = DateTime.UtcNow.Date;
                var thisWeekStart = today.AddDays(-(int)today.DayOfWeek);
                var thisMonthStart = new DateTime(today.Year, today.Month, 1);
                
                var executionsToday = await dbContext.FlowExecutions
                    .Where(e => e.FlowExecutionUsers.Any(u => u.UserId == userId) &&
                               e.Started >= today)
                    .CountAsync(cancellationToken);
                
                var executionsThisWeek = await dbContext.FlowExecutions
                    .Where(e => e.FlowExecutionUsers.Any(u => u.UserId == userId) &&
                               e.Started >= thisWeekStart)
                    .CountAsync(cancellationToken);
                
                var executionsThisMonth = await dbContext.FlowExecutions
                    .Where(e => e.FlowExecutionUsers.Any(u => u.UserId == userId) &&
                               e.Started >= thisMonthStart)
                    .CountAsync(cancellationToken);
                
                // Get daily execution trends for the last 7 days
                var trendCutoff = DateTime.UtcNow.AddDays(-7).Date;
                var executionTrends = await dbContext.FlowExecutions
                    .Where(e => e.FlowExecutionUsers.Any(u => u.UserId == userId) &&
                               e.Started >= trendCutoff)
                    .GroupBy(e => e.Started.Date)
                    .Select(g => new Models.DailyExecutionTrend
                    {
                        Date = g.Key,
                        SuccessCount = g.Count(e => e.Status == WorkflowStatus.Success.ToString()),
                        FailureCount = g.Count(e => e.Status == WorkflowStatus.Error.ToString()),
                        TotalCount = g.Count()
                    })
                    .OrderBy(t => t.Date)
                    .ToListAsync(cancellationToken);
                
                // Get top flows by execution count - join with Flows table to get names
                var topFlows = await (from fe in dbContext.FlowExecutions
                                     join f in dbContext.Flows on fe.FlowId equals f.Id
                                     where fe.FlowExecutionUsers.Any(u => u.UserId == userId) &&
                                           fe.Started >= cutoffDate
                                     group fe by new { fe.FlowId, f.Name } into g
                                     orderby g.Count() descending
                                     select new Models.FlowHealthMetric
                                     {
                                         FlowId = g.Key.FlowId,
                                         FlowName = g.Key.Name,
                                         ExecutionCount = g.Count(),
                                         SuccessRate = g.Count() > 0 ? 
                                             (double)g.Count(e => e.Status == WorkflowStatus.Success.ToString()) / g.Count() * 100 : 0,
                                         LastExecuted = g.Max(e => e.Started)
                                     })
                                     .Take(5)
                                     .ToListAsync(cancellationToken);

                return new Models.DashboardStats
                {
                    TotalFlows = totalFlows,
                    TotalRunners = totalRunners,
                    OnlineRunners = onlineRunners,
                    SuccessRate = Math.Round(successRate, 1),
                    RecentExecutions = totalExecutions,
                    TotalApps = totalApps,
                    TotalCustomSteps = totalCustomSteps,
                    ActiveSchedules = activeSchedules,
                    ExecutionsToday = executionsToday,
                    ExecutionsThisWeek = executionsThisWeek,
                    ExecutionsThisMonth = executionsThisMonth,
                    ExecutionTrends = executionTrends,
                    TopFlows = topFlows,
                    // Apps-focused metrics
                    PublishedApps = publishedApps,
                    TotalAppViews = totalAppViews,
                    RecentApps = recentApps,
                    PublishedAppsList = publishedAppsList
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