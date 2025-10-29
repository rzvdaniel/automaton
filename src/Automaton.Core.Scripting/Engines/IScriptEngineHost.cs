namespace Automaton.Core.Scripting.Engines
{
    /// <summary>
    /// Abstraction for script engine to support dependency injection and testing.
    /// </summary>
    public interface IScriptEngineHost
    {
        event EventHandler<string> ScriptTextWritten;

        IDictionary<string, string> Execute(ScriptResource resource, IDictionary<string, string> inputs);
    }
}
