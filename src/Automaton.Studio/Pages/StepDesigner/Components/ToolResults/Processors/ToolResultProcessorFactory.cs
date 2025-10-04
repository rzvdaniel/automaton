using Automaton.Mcp.Client.Models;

namespace Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Processors;

/// <summary>
/// Factory for creating tool result processors
/// </summary>
public class ToolResultProcessorFactory
{
    private readonly Dictionary<string, IToolResultProcessor> _processors;
    private readonly IToolResultProcessor _genericProcessor;

    public ToolResultProcessorFactory()
    {
        _processors = new Dictionary<string, IToolResultProcessor>
        {
            ["get_current_code"] = new GetCurrentCodeProcessor(),
            ["execute_code"] = new ExecuteCodeProcessor(),
            ["get_step_variables"] = new GetStepVariablesProcessor(),
            ["save_code"] = new SaveCodeProcessor()
        };
        
        _genericProcessor = new GenericToolProcessor();
    }

    public ToolResultViewModel ProcessToolResult(string toolName, ToolResult toolResult, string currentLanguage)
    {
        var processor = _processors.TryGetValue(toolName, out var specificProcessor) 
            ? specificProcessor 
            : _genericProcessor;

        return processor.ProcessResult(toolResult, currentLanguage);
    }

    public IEnumerable<string> GetSupportedTools()
    {
        return _processors.Keys;
    }
}