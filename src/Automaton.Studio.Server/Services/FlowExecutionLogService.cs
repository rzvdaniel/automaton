using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services
{
    public class FlowExecutionLogService
    {
        private readonly ApplicationDbContext dbContext;
        private readonly Guid userId;
        private readonly Serilog.ILogger logger;

        public FlowExecutionLogService(
            ApplicationDbContext dbContext,
            UserContextService userContextService)
        {
            this.dbContext = dbContext;
            this.userId = userContextService.GetUserId();
            this.logger = Serilog.Log.ForContext<FlowExecutionLogService>();
        }

        public async Task<IEnumerable<Models.FlowExecutionLogEntry>> GetExecutionLogsAsync(Guid executionId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Verify user has access to this execution
                var hasAccess = await dbContext.FlowExecutionUsers
                    .AnyAsync(feu => feu.FlowExecutionId == executionId && feu.UserId == userId, cancellationToken);

                if (!hasAccess)
                {
                    logger.Warning("User {UserId} attempted to access execution logs for {ExecutionId} without permission", userId, executionId);
                    return Enumerable.Empty<Models.FlowExecutionLogEntry>();
                }

                var executionLog = await dbContext.FlowExecutionLogs
                    .FirstOrDefaultAsync(log => log.FlowExecutionId == executionId, cancellationToken);

                if (executionLog == null)
                {
                    return Enumerable.Empty<Models.FlowExecutionLogEntry>();
                }

                var logEntries = executionLog.GetLogEntries();
                return logEntries.Select(MapToModel);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to retrieve execution logs for {ExecutionId}", executionId);
                return Enumerable.Empty<Models.FlowExecutionLogEntry>();
            }
        }

        public async Task<string> GetExecutionLogsTextAsync(Guid executionId, CancellationToken cancellationToken = default)
        {
            try
            {
                var logs = await GetExecutionLogsAsync(executionId, cancellationToken);
                
                var logsText = string.Join(Environment.NewLine, 
                    logs.Select(log => $"{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{log.Level}] {log.Message}" +
                                      (string.IsNullOrEmpty(log.StepName) ? "" : $" (Step: {log.StepName})") +
                                      (string.IsNullOrEmpty(log.Exception) ? "" : Environment.NewLine + log.Exception)));

                return logsText;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to generate execution logs text for {ExecutionId}", executionId);
                return "Error retrieving execution logs";
            }
        }

        public async Task<string> GetExecutionLogsJsonAsync(Guid executionId, CancellationToken cancellationToken = default)
        {
            try
            {
                // Verify user has access to this execution
                var hasAccess = await dbContext.FlowExecutionUsers
                    .AnyAsync(feu => feu.FlowExecutionId == executionId && feu.UserId == userId, cancellationToken);

                if (!hasAccess)
                {
                    logger.Warning("User {UserId} attempted to access execution logs for {ExecutionId} without permission", userId, executionId);
                    return "[]";
                }

                var executionLog = await dbContext.FlowExecutionLogs
                    .FirstOrDefaultAsync(log => log.FlowExecutionId == executionId, cancellationToken);

                return executionLog?.LogsJson ?? "[]";
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to retrieve execution logs JSON for {ExecutionId}", executionId);
                return "[]";
            }
        }

        private static Models.FlowExecutionLogEntry MapToModel(Entities.LogEntry entity)
        {
            return new Models.FlowExecutionLogEntry
            {
                Timestamp = entity.Timestamp,
                Level = entity.Level,
                Message = entity.Message,
                Exception = entity.Exception,
                StepId = entity.StepId,
                StepName = entity.StepName
            };
        }
    }
}