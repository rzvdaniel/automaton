using Automaton.Core.Scripting.Step;

namespace Automaton.Core.Scripting;

public class ScriptEngineHost : IScriptEngineHost
{
    private readonly ScriptEngineConfiguration configuration;

    public event EventHandler<string>? ScriptTextWritten;

    public ScriptEngineHost(ScriptEngineConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public IDictionary<string, dynamic> Execute(ScriptResource resource, IDictionary<string, object> inputs)
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
