namespace Automaton.Core.Scripting;

public class ScriptEngineHost
{
    private readonly ScriptEngineFactory engineFactory;

    public event EventHandler<string> ScriptTextWritten;

    public ScriptEngineHost(ScriptEngineFactory engineFactory)
    {
        this.engineFactory = engineFactory;

        engineFactory.EventRaisingStreamWriter.NewText += OnTextWritten;
    }

    public IDictionary<string, dynamic> Execute(ScriptResource resource, IDictionary<string, object> inputs)
    {
        try
        {
            var engine = engineFactory.GetEngine(resource.ContentType);

            // Set output callback if supported
            if (engine is IOutputCallbackScriptEngine outputEngine)
            {
                outputEngine.OutputCallback = line => ScriptTextWritten?.Invoke(this, line);
            }

            return engine.Execute(resource.Content, inputs);
        }
        catch (Exception ex)
        {
            ScriptTextWritten?.Invoke(this, ex.Message);
            throw;
        }
    }

    private void OnTextWritten(object? sender, string e)
    {
        ScriptTextWritten?.Invoke(this, e);
    }
}
