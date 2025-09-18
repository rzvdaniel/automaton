using Automaton.Studio.Models;
using System.Collections.Concurrent;
using System.Text;

namespace Automaton.Studio.Services
{
    public class WorkflowLogsManager
    {
        private readonly ConcurrentDictionary<Guid, List<FlowExecutionLogEntry>> executionLogs = new();
        private Guid? currentExecutionId;
        
        public event EventHandler<LogsUpdatedEventArgs>? LogsUpdated;

        /// <summary>
        /// Gets formatted logs for the current execution
        /// </summary>
        public string Logs 
        { 
            get 
            {
                if (!currentExecutionId.HasValue || !executionLogs.TryGetValue(currentExecutionId.Value, out var logs))
                    return string.Empty;

                return FormatLogs(logs);
            }
        }

        /// <summary>
        /// Sets the current execution to display logs for
        /// </summary>
        public void SetCurrentExecution(Guid executionId)
        {
            currentExecutionId = executionId;
            NotifyLogsUpdated();
        }

        /// <summary>
        /// Adds a new log entry for a specific execution
        /// </summary>
        public async Task AddLogEntry(Guid workflowId, Guid executionId, FlowExecutionLogEntry logEntry)
        {
            executionLogs.AddOrUpdate(
                executionId,
                new List<FlowExecutionLogEntry> { logEntry },
                (key, existing) => 
                {
                    existing.Add(logEntry);
                    return existing;
                });

            // If this is the current execution being displayed, notify UI
            if (currentExecutionId == executionId)
            {
                NotifyLogsUpdated();
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets logs for a specific execution
        /// </summary>
        public IEnumerable<FlowExecutionLogEntry> GetLogsForExecution(Guid executionId)
        {
            if (executionLogs.TryGetValue(executionId, out var logs))
                return logs.ToList();
                
            return Enumerable.Empty<FlowExecutionLogEntry>();
        }

        /// <summary>
        /// Clears logs for the current execution
        /// </summary>
        public void ClearLogs()
        {
            if (currentExecutionId.HasValue)
            {
                executionLogs.TryRemove(currentExecutionId.Value, out _);
                NotifyLogsUpdated();
            }
        }

        /// <summary>
        /// Clears logs for a specific execution
        /// </summary>
        public void ClearLogsForExecution(Guid executionId)
        {
            executionLogs.TryRemove(executionId, out _);
            
            if (currentExecutionId == executionId)
            {
                NotifyLogsUpdated();
            }
        }

        /// <summary>
        /// Clears all stored logs
        /// </summary>
        public void ClearAllLogs()
        {
            executionLogs.Clear();
            NotifyLogsUpdated();
        }

        private void NotifyLogsUpdated()
        {
            LogsUpdated?.Invoke(this, new LogsUpdatedEventArgs(currentExecutionId, Logs));
        }

        private static string FormatLogs(IEnumerable<FlowExecutionLogEntry> logs)
        {
            var sb = new StringBuilder();
            
            foreach (var log in logs.OrderBy(l => l.Timestamp))
            {
                var formattedTime = log.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss");
                var stepInfo = !string.IsNullOrEmpty(log.StepName) ? $" [{log.StepName}]" : "";
                
                sb.AppendLine($"{formattedTime} [{log.Level}]{stepInfo} {log.Message}");
                
                // Add exception details if present
                if (!string.IsNullOrEmpty(log.Exception))
                {
                    var exceptionLines = log.Exception.Split('\n');
                    foreach (var line in exceptionLines)
                    {
                        sb.AppendLine($"    {line}");
                    }
                }
            }
            
            return sb.ToString();
        }
    }

    public class LogsUpdatedEventArgs : EventArgs
    {
        public Guid? ExecutionId { get; }
        public string FormattedLogs { get; }

        public LogsUpdatedEventArgs(Guid? executionId, string formattedLogs)
        {
            ExecutionId = executionId;
            FormattedLogs = formattedLogs;
        }
    }
}