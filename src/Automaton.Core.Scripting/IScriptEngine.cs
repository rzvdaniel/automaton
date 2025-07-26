namespace Automaton.Core.Scripting
{
    public interface IScriptEngine
    {
        IDictionary<string, object> Execute(string code, IDictionary<string, object> inputs);
    }
}
