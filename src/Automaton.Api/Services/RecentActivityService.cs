using Automaton.Api.Data;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Api.Services
{
    public class RecentActivityService
    {
        private readonly ApplicationDbContext dbContext;
        private readonly UserContextService userContextService;
        private readonly Serilog.ILogger logger;
        private readonly Guid userId;

        public RecentActivityService(
            ApplicationDbContext dbContext,
            UserContextService userContextService)
        {
            this.dbContext = dbContext;
            this.userContextService = userContextService;
            this.logger = Log.ForContext<RecentActivityService>();
            this.userId = userContextService.GetUserId();
        }

        public async Task<IEnumerable<Models.RecentActivity>> GetRecentActivitiesAsync(int count = 20, CancellationToken cancellationToken = default)
        {
            try
            {
                var activities = await dbContext.RecentActivities
                    .Include(a => a.User)
                    .Where(a => a.UserId == userId)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(count)
                    .ToListAsync(cancellationToken);

                return activities.Select(MapToModel);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to get recent activities for user {UserId}", userId);
                return new List<Models.RecentActivity>();
            }
        }

        public async Task LogActivityAsync(
            ActivityType activityType,
            string entityName,
            Guid? entityId = null,
            string description = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var activity = new Entities.RecentActivity
                {
                    Id = Guid.NewGuid(),
                    ActivityType = activityType,
                    EntityName = entityName,
                    EntityId = entityId,
                    Description = description ?? GenerateDescription(activityType, entityName),
                    CreatedAt = DateTime.UtcNow,
                    UserId = userId
                };

                dbContext.RecentActivities.Add(activity);
                await dbContext.SaveChangesAsync(cancellationToken);

                // Clean up old activities to keep the table manageable
                await CleanupOldActivitiesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to log activity {ActivityType} for {EntityName}", activityType, entityName);
            }
        }

        public async Task LogFlowActivityAsync(ActivityType activityType, string flowName, Guid flowId, CancellationToken cancellationToken = default)
        {
            await LogActivityAsync(activityType, flowName, flowId, cancellationToken: cancellationToken);
        }

        public async Task LogCustomStepActivityAsync(ActivityType activityType, string stepName, Guid stepId, CancellationToken cancellationToken = default)
        {
            await LogActivityAsync(activityType, stepName, stepId, cancellationToken: cancellationToken);
        }

        public async Task LogRunnerActivityAsync(ActivityType activityType, string runnerName, Guid runnerId, CancellationToken cancellationToken = default)
        {
            await LogActivityAsync(activityType, runnerName, runnerId, cancellationToken: cancellationToken);
        }

        public async Task LogScheduleActivityAsync(ActivityType activityType, string scheduleName, Guid scheduleId, CancellationToken cancellationToken = default)
        {
            await LogActivityAsync(activityType, scheduleName, scheduleId, cancellationToken: cancellationToken);
        }

        public async Task LogAppActivityAsync(ActivityType activityType, string appName, Guid appId, CancellationToken cancellationToken = default)
        {
            await LogActivityAsync(activityType, appName, appId, cancellationToken: cancellationToken);
        }

        private static Models.RecentActivity MapToModel(Entities.RecentActivity entity)
        {
            return new Models.RecentActivity
            {
                Id = entity.Id,
                ActivityType = entity.ActivityType,
                EntityName = entity.EntityName,
                EntityId = entity.EntityId,
                Description = entity.Description,
                CreatedAt = entity.CreatedAt,
                UserId = entity.UserId,
                UserName = entity.User?.UserName ?? string.Empty
            };
        }

        private string GenerateDescription(ActivityType activityType, string entityName)
        {
            return activityType switch
            {
                ActivityType.FlowCreated => $"Created flow '{entityName}'",
                ActivityType.FlowUpdated => $"Updated flow '{entityName}'",
                ActivityType.FlowDeleted => $"Deleted flow '{entityName}'",
                ActivityType.FlowExecuted => $"Executed flow '{entityName}'",
                ActivityType.CustomStepCreated => $"Created custom step '{entityName}'",
                ActivityType.CustomStepUpdated => $"Updated custom step '{entityName}'",
                ActivityType.CustomStepDeleted => $"Deleted custom step '{entityName}'",
                ActivityType.RunnerAdded => $"Added runner '{entityName}'",
                ActivityType.RunnerUpdated => $"Updated runner '{entityName}'",
                ActivityType.RunnerDeleted => $"Deleted runner '{entityName}'",
                ActivityType.ScheduleCreated => $"Created schedule '{entityName}'",
                ActivityType.ScheduleUpdated => $"Updated schedule '{entityName}'",
                ActivityType.ScheduleDeleted => $"Deleted schedule '{entityName}'",
                ActivityType.AppCreated => $"Created app '{entityName}'",
                ActivityType.AppUpdated => $"Updated app '{entityName}'",
                ActivityType.AppDeleted => $"Deleted app '{entityName}'",
                ActivityType.AppPublished => $"Published app '{entityName}'",
                ActivityType.AppUnpublished => $"Unpublished app '{entityName}'",
                _ => $"Performed action on '{entityName}'"
            };
        }

        private async Task CleanupOldActivitiesAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Keep only the most recent 100 activities per user
                var cutoffDate = DateTime.UtcNow.AddDays(-30); // Also remove anything older than 30 days
                
                var oldActivities = await dbContext.RecentActivities
                    .Where(a => a.UserId == userId && 
                        (a.CreatedAt < cutoffDate || 
                         a.Id.ToString().CompareTo(
                             dbContext.RecentActivities
                                 .Where(b => b.UserId == userId)
                                 .OrderByDescending(b => b.CreatedAt)
                                 .Skip(100)
                                 .Take(1)
                                 .Select(b => b.Id.ToString())
                                 .FirstOrDefault() ?? string.Empty
                         ) < 0))
                    .ToListAsync(cancellationToken);

                if (oldActivities.Any())
                {
                    dbContext.RecentActivities.RemoveRange(oldActivities);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to cleanup old activities for user {UserId}", userId);
            }
        }
    }
}