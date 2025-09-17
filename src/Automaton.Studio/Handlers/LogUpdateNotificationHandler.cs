using Automaton.Studio.Events;
using Automaton.Studio.Services;

namespace Automaton.Studio.Handlers
{
    public class LogUpdateNotificationHandler : INotificationHandler<LogUpdateNotification>
    {
        private readonly WorkflowLogsManager workflowLogsManager;

        public LogUpdateNotificationHandler(WorkflowLogsManager workflowLogsManager)
        {
            this.workflowLogsManager = workflowLogsManager;
        }

        public async Task Handle(LogUpdateNotification notification, CancellationToken cancellationToken)
        {
            await workflowLogsManager.AddLogEntry(
                notification.WorkflowId, 
                notification.WorkflowExecutionId, 
                notification.LogEntry);
        }
    }
}