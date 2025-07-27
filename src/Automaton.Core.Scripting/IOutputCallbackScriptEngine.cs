namespace Automaton.Core.Scripting
{
    public interface IOutputCallbackScriptEngine
    {
        Action<string>? OutputCallback { get; set; }
    }
}