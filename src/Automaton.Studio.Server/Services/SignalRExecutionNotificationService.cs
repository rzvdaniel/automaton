using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Studio.Server.Services
{
    public class SignalRExecutionNotificationService : IExecutionNotificationService
    {
        private readonly IHubContext<Hubs.AutomatonHub> hubContext;
        private readonly string connectionId;

        public SignalRExecutionNotificationService(
            IHubContext<Hubs.AutomatonHub> hubContext,
            string connectionId = null)
        {
            this.hubContext = hubContext;
            this.connectionId = connectionId;
        }

        public async Task SendLogUpdate(Guid workflowId, Guid executionId, string level, string message,
            string stepId = null, string stepName = null, string exception = null)
        {
            var logData = new
            {
                WorkflowId = workflowId,
                ExecutionId = executionId,
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message,
                StepId = stepId,
                StepName = stepName,
                Exception = exception
            };

            if (!string.IsNullOrEmpty(connectionId))
            {
                // Send to specific connection (console runner)
                await hubContext.Clients.Client(connectionId)
                    .SendAsync("LogUpdate", logData);
            }
            else
            {
                // Broadcast to all connected clients
                await hubContext.Clients.All
                    .SendAsync("LogUpdate", logData);
            }
        }

        public async Task SendStepExecution(string stepId)
        {
            var stepData = new { StepId = stepId };

            if (!string.IsNullOrEmpty(connectionId))
            {
                await hubContext.Clients.Client(connectionId)
                    .SendAsync("StepExecution", stepData);
            }
            else
            {
                await hubContext.Clients.All
                    .SendAsync("StepExecution", stepData);
            }
        }

        public async Task SendVariableUpdate(StepVariable variable)
        {
            var variableData = new
            {
                Variable = new
                {
                    variable.Id,
                    variable.Name,
                    variable.Value,
                    variable.Type
                }
            };

            if (!string.IsNullOrEmpty(connectionId))
            {
                await hubContext.Clients.Client(connectionId)
                    .SendAsync("VariableUpdate", variableData);
            }
            else
            {
                await hubContext.Clients.All
                    .SendAsync("VariableUpdate", variableData);
            }
        }
    }
}