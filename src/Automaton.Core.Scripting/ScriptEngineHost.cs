namespace Automaton.Core.Scripting;

public class ScriptEngineHost
{
    private const string BuiltinsVariable = "__builtins__";
    private const string ArgvVariable = "argv";

    private readonly ScriptEngineFactory engineFactory;

    public event EventHandler<string> ScriptTextWritten;

    public ScriptEngineHost(ScriptEngineFactory engineFactory)
    {
        this.engineFactory = engineFactory;

        engineFactory.EventRaisingStreamWriter.NewText += OnTextWritten;
    }

    public IDictionary<string, dynamic> Execute(ScriptResource resource, IDictionary<string, object> inputs)
    {
        var engine = engineFactory.GetEngine(resource.ContentType);
        return engine.Execute(resource.Content, inputs);
    }

    private void OnTextWritten(object? sender, string e)
    {
        ScriptTextWritten?.Invoke(this, e);
    }
}
