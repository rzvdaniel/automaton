using Automaton.Studio.Models;
using System.Collections.Concurrent;
using System.Text;

namespace Automaton.Studio.Services
{
    public class WorkflowLogsManager
    {
        private readonly ConcurrentDictionary<Guid, List<FlowExecutionLogEntry>> _executionLogs = new();
        private Guid? _currentExecutionId;
        
        public event EventHandler<LogsUpdatedEventArgs>? LogsUpdated;

        /// <summary>
        /// Gets formatted logs for the current execution
        /// </summary>
        public string Logs 
        { 
            get 
            {
                if (!_currentExecutionId.HasValue || !_executionLogs.TryGetValue(_currentExecutionId.Value, out var logs))
                    return string.Empty;

                return FormatLogs(logs);
            }
        }

        /// <summary>
        /// Sets the current execution to display logs for
        /// </summary>
        public void SetCurrentExecution(Guid executionId)
        {
            _currentExecutionId = executionId;
            NotifyLogsUpdated();
        }

        /// <summary>
        /// Adds a new log entry for a specific execution
        /// </summary>
        public async Task AddLogEntry(Guid workflowId, Guid executionId, FlowExecutionLogEntry logEntry)
        {
            _executionLogs.AddOrUpdate(
                executionId,
                new List<FlowExecutionLogEntry> { logEntry },
                (key, existing) => 
                {
                    existing.Add(logEntry);
                    return existing;
                });

            // If this is the current execution being displayed, notify UI
            if (_currentExecutionId == executionId)
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
            if (_executionLogs.TryGetValue(executionId, out var logs))
                return logs.ToList();
                
            return Enumerable.Empty<FlowExecutionLogEntry>();
        }

        /// <summary>
        /// Clears logs for the current execution
        /// </summary>
        public void ClearLogs()
        {
            if (_currentExecutionId.HasValue)
            {
                _executionLogs.TryRemove(_currentExecutionId.Value, out _);
                NotifyLogsUpdated();
            }
        }

        /// <summary>
        /// Clears logs for a specific execution
        /// </summary>
        public void ClearLogsForExecution(Guid executionId)
        {
            _executionLogs.TryRemove(executionId, out _);
            
            if (_currentExecutionId == executionId)
            {
                NotifyLogsUpdated();
            }
        }

        /// <summary>
        /// Clears all stored logs
        /// </summary>
        public void ClearAllLogs()
        {
            _executionLogs.Clear();
            NotifyLogsUpdated();
        }

        private void NotifyLogsUpdated()
        {
            LogsUpdated?.Invoke(this, new LogsUpdatedEventArgs(_currentExecutionId, Logs));
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