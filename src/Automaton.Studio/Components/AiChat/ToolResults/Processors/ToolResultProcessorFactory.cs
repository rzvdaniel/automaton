using Automaton.Mcp.Models;

namespace Automaton.Studio.Components.AiChat.ToolResults.Processors;

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
        if (_processors.TryGetValue(toolName, out var specificProcessor))
        {
            return specificProcessor.ProcessResult(toolResult, currentLanguage);
        }
        
        // For generic processor, pass the actual tool name for better display
        return ProcessGenericResult(toolName, toolResult);
    }
    
    private ToolResultViewModel ProcessGenericResult(string toolName, ToolResult toolResult)
    {
        // Format tool name for display (e.g., "suggest_variables" -> "Suggest variables")
        var displayName = FormatToolNameForDisplay(toolName);
        
        return new ToolResultViewModel
        {
            Icon = "tool",
            Description = displayName,
            StatusIcon = toolResult.IsSuccess ? "check-circle" : "close-circle",
            Status = toolResult.IsSuccess ? "Success" : "Failed",
            IsSuccess = toolResult.IsSuccess,
            ResultType = ToolResultType.Generic,
            Summary = toolResult.IsSuccess ? "Tool executed successfully" : null,
            ErrorMessage = toolResult.IsSuccess ? null : toolResult.ErrorMessage
        };
    }
    
    private string FormatToolNameForDisplay(string toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return "Executing tool";
            
        // Convert snake_case to readable format (e.g., "suggest_variables" -> "Suggest variables")
        var words = toolName.Split('_');
        if (words.Length > 0)
        {
            words[0] = char.ToUpper(words[0][0]) + words[0].Substring(1);
        }
        return string.Join(" ", words);
    }

    public IEnumerable<string> GetSupportedTools()
    {
        return _processors.Keys;
    }
}