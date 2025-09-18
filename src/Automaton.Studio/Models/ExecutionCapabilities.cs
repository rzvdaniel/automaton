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
        public bool HasSelectedRunners { get; set; }

        /// <summary>
        /// Whether the flow should be executed locally (mutually exclusive with runner execution)
        /// Priority: If runners are selected, use runners; otherwise use local if available
        /// </summary>
        public bool ShouldRunLocally => CanRunLocally && !HasSelectedRunners;

        /// <summary>
        /// Whether the flow should be executed on runners (mutually exclusive with local execution)
        /// </summary>
        public bool ShouldRunOnRunners => HasSelectedRunners;

        /// <summary>
        /// Whether any execution option is available
        /// </summary>
        public bool CanExecute => ShouldRunLocally || ShouldRunOnRunners;

        /// <summary>
        /// Gets the appropriate button text based on execution capabilities
        /// </summary>
        public string GetButtonText()
        {
            if (ShouldRunOnRunners)
                return "Run on Runners";
            
            if (ShouldRunLocally)
                return "Run Locally";
            
            return "Run";
        }

        /// <summary>
        /// Gets a description of what will happen when run is clicked
        /// </summary>
        public string GetExecutionDescription()
        {
            if (ShouldRunOnRunners)
                return "Will execute on all selected runners";

            if (ShouldRunLocally)
                return "Will execute locally";  
            
            return "No execution method available";
        }
    }
}