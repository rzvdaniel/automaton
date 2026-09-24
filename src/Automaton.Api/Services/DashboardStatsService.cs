using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Api.Services
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

        public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var totalFlows = await GetTotalFlowsAsync(cancellationToken);

                // Use RunnerService to get accurate runner status information
                // This leverages the existing GetRunnersWithUpdatedStatus logic that pings runners
                var runners = await runnerService.List(cancellationToken);
                var runnerMetrics = GetAppOwnerRunnerMetrics(runners);
                var totalRunners = runnerMetrics.Total;
                var onlineRunners = runnerMetrics.Online;

                var activityMetrics = await GetActivityMetricsAsync(cancellationToken);
                var applicationMetrics = await GetApplicationMetricsAsync(cancellationToken);
                var resourceCounts = await GetResourceCountsAsync(cancellationToken);
                
                return new Models.DashboardStats
                {
                    TotalFlows = totalFlows,
                    TotalRunners = totalRunners,
                    OnlineRunners = onlineRunners,
                    SuccessRate = Math.Round(activityMetrics.SuccessRate, 1),
                    RecentExecutions = activityMetrics.TotalExecutions,
                    TotalApps = applicationMetrics.TotalApps,
                    TotalCustomSteps = resourceCounts.CustomSteps,
                    ActiveSchedules = resourceCounts.Schedules,
                    ExecutionsToday = activityMetrics.ExecutionsToday,
                    ExecutionsThisWeek = activityMetrics.ExecutionsThisWeek,
                    ExecutionsThisMonth = activityMetrics.ExecutionsThisMonth,
                    ExecutionTrends = activityMetrics.ExecutionTrends,
                    TopFlows = activityMetrics.TopFlows,
                    // Apps-focused metrics
                    PublishedApps = applicationMetrics.PublishedApps,
                    TotalAppViews = applicationMetrics.TotalAppViews,
                    RecentApps = applicationMetrics.RecentApps,
                    PublishedAppsList = applicationMetrics.PublishedAppsList
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to get dashboard stats for user {UserId}", userId);
                return new Models.DashboardStats();
            }
        }

        private Task<int> GetTotalFlowsAsync(CancellationToken cancellationToken)
            => dbContext.Flows
                .Where(flow => flow.FlowUsers.Any(user => user.UserId == userId))
                .CountAsync(cancellationToken);

        private async Task<(int CustomSteps, int Schedules)> GetResourceCountsAsync(CancellationToken cancellationToken)
        {
            var customSteps = await dbContext.CustomSteps
                .Where(step => step.CustomStepUsers.Any(user => user.UserId == userId))
                .CountAsync(cancellationToken);
            var schedules = await dbContext.Schedules
                .Where(schedule => schedule.ScheduleUsers.Any(user => user.UserId == userId))
                .CountAsync(cancellationToken);
            return (customSteps, schedules);
        }

        private async Task<ActivityMetrics> GetActivityMetricsAsync(CancellationToken cancellationToken)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            var executionStats = await dbContext.FlowExecutions
                .Where(execution => execution.FlowExecutionUsers.Any(user => user.UserId == userId)
                    && execution.Started >= cutoffDate)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    TotalCount = group.Count(),
                    SuccessCount = group.Count(execution => execution.Status == WorkflowStatus.Success.ToString())
                })
                .FirstOrDefaultAsync(cancellationToken);
            var totalExecutions = executionStats?.TotalCount ?? 0;
            var successRate = totalExecutions > 0
                ? (double)(executionStats?.SuccessCount ?? 0) / totalExecutions * 100
                : 0;

            var today = DateTime.UtcNow.Date;
            var thisWeekStart = today.AddDays(-(int)today.DayOfWeek);
            var thisMonthStart = new DateTime(today.Year, today.Month, 1);
            var executionsToday = await CountExecutionsSinceAsync(today, cancellationToken);
            var executionsThisWeek = await CountExecutionsSinceAsync(thisWeekStart, cancellationToken);
            var executionsThisMonth = await CountExecutionsSinceAsync(thisMonthStart, cancellationToken);
            var executionTrends = await GetExecutionTrendsAsync(cancellationToken);
            var topFlows = await GetTopFlowsAsync(cutoffDate, cancellationToken);

            return new ActivityMetrics(
                totalExecutions, successRate, executionsToday, executionsThisWeek, executionsThisMonth,
                executionTrends, topFlows);
        }

        private Task<int> CountExecutionsSinceAsync(DateTime start, CancellationToken cancellationToken)
            => dbContext.FlowExecutions
                .Where(execution => execution.FlowExecutionUsers.Any(user => user.UserId == userId)
                    && execution.Started >= start)
                .CountAsync(cancellationToken);

        private Task<List<Models.DailyExecutionTrend>> GetExecutionTrendsAsync(CancellationToken cancellationToken)
        {
            var trendCutoff = DateTime.UtcNow.AddDays(-7).Date;
            return dbContext.FlowExecutions
                .Where(execution => execution.FlowExecutionUsers.Any(user => user.UserId == userId)
                    && execution.Started >= trendCutoff)
                .GroupBy(execution => execution.Started.Date)
                .Select(group => new Models.DailyExecutionTrend
                {
                    Date = group.Key,
                    SuccessCount = group.Count(execution => execution.Status == WorkflowStatus.Success.ToString()),
                    FailureCount = group.Count(execution => execution.Status == WorkflowStatus.Error.ToString()),
                    TotalCount = group.Count()
                })
                .OrderBy(trend => trend.Date)
                .ToListAsync(cancellationToken);
        }

        private Task<List<Models.FlowHealthMetric>> GetTopFlowsAsync(DateTime cutoffDate, CancellationToken cancellationToken)
            => (from execution in dbContext.FlowExecutions
                join flow in dbContext.Flows on execution.FlowId equals flow.Id
                where execution.FlowExecutionUsers.Any(user => user.UserId == userId)
                    && execution.Started >= cutoffDate
                group execution by new { execution.FlowId, flow.Name } into flowGroup
                orderby flowGroup.Count() descending
                select new Models.FlowHealthMetric
                {
                    FlowId = flowGroup.Key.FlowId,
                    FlowName = flowGroup.Key.Name,
                    ExecutionCount = flowGroup.Count(),
                    SuccessRate = flowGroup.Count() > 0
                        ? (double)flowGroup.Count(execution => execution.Status == WorkflowStatus.Success.ToString()) / flowGroup.Count() * 100
                        : 0,
                    LastExecuted = flowGroup.Max(execution => execution.Started)
                })
                .Take(5)
                .ToListAsync(cancellationToken);

        private sealed record ActivityMetrics(
            int TotalExecutions,
            double SuccessRate,
            int ExecutionsToday,
            int ExecutionsThisWeek,
            int ExecutionsThisMonth,
            List<Models.DailyExecutionTrend> ExecutionTrends,
            List<Models.FlowHealthMetric> TopFlows);

        private async Task<ApplicationMetrics> GetApplicationMetricsAsync(CancellationToken cancellationToken)
        {
            var userApps = dbContext.WebApps.Where(app => app.WebAppUsers.Any(user => user.UserId == userId));
            var totalApps = await userApps.CountAsync(cancellationToken);
            var publishedApps = await userApps.Where(app => app.IsPublished).CountAsync(cancellationToken);
            var totalAppViews = await userApps.SumAsync(app => app.ViewCount, cancellationToken);
            var recentApps = await userApps
                .OrderByDescending(app => app.Updated)
                .Take(5)
                .Select(app => new Models.RecentAppMetric
                {
                    Id = app.Id,
                    Name = app.Name,
                    Summary = app.Summary,
                    IsPublished = app.IsPublished,
                    PublicUrl = app.PublicUrl,
                    ViewCount = app.ViewCount,
                    Updated = app.Updated
                })
                .ToListAsync(cancellationToken);
            var publishedAppsList = await userApps
                .Where(app => app.IsPublished)
                .OrderByDescending(app => app.ViewCount)
                .Take(5)
                .Select(app => new Models.PublishedAppMetric
                {
                    Id = app.Id,
                    Name = app.Name,
                    Summary = app.Summary,
                    PublicUrl = app.PublicUrl!,
                    ViewCount = app.ViewCount,
                    PublishedAt = app.PublishedAt
                })
                .ToListAsync(cancellationToken);

            return new ApplicationMetrics(totalApps, publishedApps, totalAppViews, recentApps, publishedAppsList);
        }

        private sealed record ApplicationMetrics(
            int TotalApps,
            int PublishedApps,
            int TotalAppViews,
            List<Models.RecentAppMetric> RecentApps,
            List<Models.PublishedAppMetric> PublishedAppsList);

        internal static (int Total, int Online) GetAppOwnerRunnerMetrics(IEnumerable<RunnerDetails> runners)
        {
            // Keep Dashboard counts aligned with the app-owner Runners surface, which hides
            // Infrastructure Runners because they are platform provisioning infrastructure.
            var appOwnerRunners = runners
                .Where(runner => !RunnerProfileRules.IsInfrastructure(runner.Purpose))
                .ToList();

            return (
                appOwnerRunners.Count,
                appOwnerRunners.Count(runner => runner.Status == RunnerStatus.Online));
        }
    }
}
