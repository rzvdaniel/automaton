namespace Automaton.Studio.Models
{
    /// <summary>
    /// Represents the execution capabilities available for a flow
    /// </summary>
    public class ExecutionCapabilities
    {
        /// <summary>
        /// Whether the flow can be executed locally (Desktop/Debug mode)
        /// </summary>
        public bool CanRunLocally { get; set; }

        /// <summary>
        /// Whether the flow can be executed on selected runners
        /// </summary>
        public bool CanRunOnRunners { get; set; }

        /// <summary>
        /// Whether the flow should be executed both locally and on runners
        /// </summary>
        public bool ShouldRunBoth { get; set; }

        /// <summary>
        /// Whether any execution option is available
        /// </summary>
        public bool CanExecute => CanRunLocally || CanRunOnRunners;

        /// <summary>
        /// Gets the appropriate button text based on execution capabilities
        /// </summary>
        public string GetButtonText()
        {
            if (ShouldRunBoth)
                return "Run (Local + Runners)";
            
            if (CanRunLocally && !CanRunOnRunners)
                return "Debug";
            
            if (CanRunOnRunners && !CanRunLocally)
                return "Run on Runners";
            
            return "Run";
        }

        /// <summary>
        /// Gets a description of what will happen when run is clicked
        /// </summary>
        public string GetExecutionDescription()
        {
            if (ShouldRunBoth)
                return "Will execute locally and on all selected runners";
            
            if (CanRunLocally && !CanRunOnRunners)
                return "Will execute locally for debugging";
            
            if (CanRunOnRunners && !CanRunLocally)
                return "Will execute on all selected runners";
            
            return "";
        }
    }
}