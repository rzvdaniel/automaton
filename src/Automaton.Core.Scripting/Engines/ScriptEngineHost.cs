namespace Automaton.Core.Scripting.Engines;

public class ScriptEngineHost : IScriptEngineHost
{
    private readonly ScriptEngineConfiguration configuration;

    public event EventHandler<string>? ScriptTextWritten;

    public ScriptEngineHost(ScriptEngineConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public ScriptExecutionResult Execute(ScriptResource resource, IDictionary<string, string> inputs)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(inputs);

        var contentType = resource.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("A script content type is required.", nameof(resource));

        var code = resource.Content;
        if (code == null)
            throw new ArgumentException("Script content is required.", nameof(resource));

        try
        {
            // Create ScriptEngineFactory on demand
            var engineFactory = new ScriptEngineFactory(configuration);
            engineFactory.EventRaisingStreamWriter.NewText += OnTextWritten;
            var engine = engineFactory.GetEngine(contentType);

            // Set output callback if supported
            if (engine is IOutputCallbackScriptEngine outputEngine)
            {
                outputEngine.OutputCallback = line => ScriptTextWritten?.Invoke(this, line);
            }

            var result = engine.ExecuteScript(code, inputs);

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
