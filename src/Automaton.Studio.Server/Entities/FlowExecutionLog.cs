#nullable disable
using System.Text.Json;
using System.Text.Json.Serialization;

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
                WriteIndented = false, // Compact JSON for storage
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull // Exclude null optional properties
            });
            UpdatedAt = DateTime.UtcNow;
        }
    }
    
    public class LogEntry
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
        
        [JsonPropertyName("level")]
        public string Level { get; set; } // Info, Warning, Error, Debug  
        
        [JsonPropertyName("message")]
        public string Message { get; set; }
        
        // Optional properties - excluded when null or empty
        [JsonPropertyName("exception")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Exception { get; set; } // Serialized exception details if any
        
        [JsonPropertyName("stepId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? StepId { get; set; } // Optional - which step generated this log
        
        [JsonPropertyName("stepName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? StepName { get; set; } // Optional - step name for easier reading
    }
}