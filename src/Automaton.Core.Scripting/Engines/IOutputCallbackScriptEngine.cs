namespace Automaton.Core.Scripting.Engines
{
    public interface IOutputCallbackScriptEngine
    {
        Action<string>? OutputCallback { get; set; }
    }
}