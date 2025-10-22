namespace Automaton.Core.Scripting
{
    /// <summary>
    /// Abstraction for script engine to support dependency injection and testing.
    /// </summary>
    public interface IScriptEngineHost
    {
        event EventHandler<string> ScriptTextWritten;

        IDictionary<string, dynamic> Execute(ScriptResource resource, IDictionary<string, object> inputs);
    }
}
