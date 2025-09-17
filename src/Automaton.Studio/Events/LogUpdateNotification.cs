using Automaton.Studio.Models;

namespace Automaton.Studio.Events
{
    public class LogUpdateNotification : INotification
    {
        public Guid WorkflowId { get; private set; }
        public Guid WorkflowExecutionId { get; private set; }
        public FlowExecutionLogEntry LogEntry { get; private set; }

        public LogUpdateNotification(Guid workflowId, Guid workflowExecutionId, FlowExecutionLogEntry logEntry)
        {
            WorkflowId = workflowId;
            WorkflowExecutionId = workflowExecutionId;
            LogEntry = logEntry;
        }
    }
}