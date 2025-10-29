namespace Automaton.Core.Scripting.Engines
{
    public interface IScriptEngine
    {
        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        [Obsolete("Use ExecuteScript method which returns strongly-typed ScriptExecutionResult")]
        IDictionary<string, string> Execute(string code, IDictionary<string, string> inputs);

        /// <summary>
        /// Execute script code with structured error handling
        /// </summary>
        /// <param name="code">Script code to execute</param>
        /// <param name="inputs">Input variables for the script</param>
        /// <returns>Structured execution result with detailed error information</returns>
        ScriptExecutionResult ExecuteScript(string code, IDictionary<string, string> inputs);
    }
}
