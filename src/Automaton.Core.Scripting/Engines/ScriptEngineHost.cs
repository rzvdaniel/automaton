namespace Automaton.Core.Scripting.Engines;

public class ScriptEngineHost : IScriptEngineHost
{
    private readonly ScriptEngineConfiguration configuration;

    public event EventHandler<string>? ScriptTextWritten;

    public ScriptEngineHost(ScriptEngineConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public IDictionary<string, string> Execute(ScriptResource resource, IDictionary<string, string> inputs)
    {
        try
        {
            // Create ScriptEngineFactory on demand
            var engineFactory = new ScriptEngineFactory(configuration);
            engineFactory.EventRaisingStreamWriter.NewText += OnTextWritten;
            var engine = engineFactory.GetEngine(resource.ContentType);

            // Set output callback if supported
            if (engine is IOutputCallbackScriptEngine outputEngine)
            {
                outputEngine.OutputCallback = line => ScriptTextWritten?.Invoke(this, line);
            }

            var result = engine.Execute(resource.Content, inputs);

            return result;
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
