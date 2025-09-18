using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Studio.Events;
using Automaton.Studio.Models;

namespace Automaton.Studio.Services
{
    public class MediatRExecutionNotificationService : IExecutionNotificationService
    {
        private readonly IMediator mediator;

        public MediatRExecutionNotificationService(IMediator mediator)
        {
            this.mediator = mediator;
        }

        public async Task SendLogUpdate(Guid workflowId, Guid executionId, string level, string message,
            string stepId = null, string stepName = null, string exception = null)
        {
            var logEntry = new FlowExecutionLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message,
                StepId = stepId,
                StepName = stepName,
                Exception = exception
            };

            await mediator.Publish(new LogUpdateNotification(workflowId, executionId, logEntry));
        }

        public async Task SendStepExecution(string stepId)
        {
            await mediator.Publish(new ExecuteStepNotification(stepId));
        }

        public async Task SendVariableUpdate(StepVariable variable)
        {
            await mediator.Publish(new VariableUpdateNotification(variable));
        }
    }
}