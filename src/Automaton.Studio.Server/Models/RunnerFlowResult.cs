using Automaton.Core.Enums;

namespace Automaton.Studio.Server.Models
{
    public class RunnerFlowResult
    {
        public Guid FlowId { get; set; }
        public Guid RunnerId { get; set; }
        public DateTime Started { get; set; }
        public DateTime Finished { get; set; }
        public WorkflowStatus Status { get; set; }
        
        /// <summary>
        /// Flow output variables - actual results from the flow execution
        /// </summary>
        public Dictionary<string, string?> OutputVariables { get; set; } = [];
     
        /// <summary>
        /// Execution logs for debugging
        /// </summary>
        public List<string> Logs { get; set; } = [];
    }
}
