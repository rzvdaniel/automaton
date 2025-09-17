#nullable disable
using System.Text.Json;

namespace Automaton.Studio.Server.Entities
{
    public class FlowExecutionLog
    {
        public Guid Id { get; set; }
        public Guid FlowExecutionId { get; set; }
        public string LogsJson { get; set; } // JSON array of log entries
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual FlowExecution FlowExecution { get; set; }
        
        public List<LogEntry> GetLogEntries()
        {
            if (string.IsNullOrEmpty(LogsJson))
                return new List<LogEntry>();
                
            try
            {
                return JsonSerializer.Deserialize<List<LogEntry>>(LogsJson) ?? new List<LogEntry>();
            }
            catch
            {
                return new List<LogEntry>();
            }
        }
        
        public void SetLogEntries(IEnumerable<LogEntry> logEntries)
        {
            LogsJson = JsonSerializer.Serialize(logEntries, new JsonSerializerOptions 
            { 
                WriteIndented = false // Compact JSON for storage
            });
            UpdatedAt = DateTime.UtcNow;
        }
    }
    
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } // Info, Warning, Error, Debug  
        public string Message { get; set; }
        public string Exception { get; set; } // Serialized exception details if any
        public string StepId { get; set; } // Optional - which step generated this log
        public string StepName { get; set; } // Optional - step name for easier reading
    }
}