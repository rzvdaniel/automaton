using Automaton.Mcp.Client.Models;

namespace Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Processors;

public class SaveCodeProcessor : IToolResultProcessor
{
    public string ToolName => "save_code";

    public ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage)
    {
        var viewModel = new ToolResultViewModel
        {
            Icon = "save",
            Description = "Saving code to step",
            StatusIcon = toolResult.IsSuccess ? "check-circle" : "close-circle",
            Status = toolResult.IsSuccess ? "Success" : "Failed",
            IsSuccess = toolResult.IsSuccess,
            ResultType = ToolResultType.SaveCode
        };

        if (toolResult.IsSuccess)
        {
            viewModel.Summary = "**Code saved successfully to Step Designer**";
        }
        else if (!toolResult.IsSuccess)
        {
            viewModel.ErrorMessage = toolResult.ErrorMessage;
        }

        return viewModel;
    }
}

public class GenericToolProcessor : IToolResultProcessor
{
    public string ToolName => "generic";

    public ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage)
    {
        return new ToolResultViewModel
        {
            Icon = "tool",
            Description = "Executing tool",
            StatusIcon = toolResult.IsSuccess ? "check-circle" : "close-circle",
            Status = toolResult.IsSuccess ? "Success" : "Failed",
            IsSuccess = toolResult.IsSuccess,
            ResultType = ToolResultType.Generic,
            Summary = toolResult.IsSuccess ? "Tool executed successfully" : null,
            ErrorMessage = toolResult.IsSuccess ? null : toolResult.ErrorMessage
        };
    }
}