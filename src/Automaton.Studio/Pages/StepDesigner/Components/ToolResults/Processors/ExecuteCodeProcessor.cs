using Automaton.Mcp.Client.Models;
using System.Text.Json;

namespace Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Processors;

public class ExecuteCodeProcessor : IToolResultProcessor
{
    public string ToolName => "execute_code";

    public ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage)
    {
        var viewModel = new ToolResultViewModel
        {
            Icon = "??",
            Description = "Testing code execution",
            StatusIcon = toolResult.IsSuccess ? "?" : "?",
            Status = toolResult.IsSuccess ? "Success" : "Failed",
            IsSuccess = toolResult.IsSuccess,
            ResultType = ToolResultType.Execution
        };

        if (toolResult.IsSuccess && !string.IsNullOrEmpty(toolResult.Result))
        {
            viewModel.Summary = GetExecutionSummary(toolResult.Result);
            viewModel.RawResult = toolResult.Result;
        }
        else if (!toolResult.IsSuccess)
        {
            viewModel.ErrorMessage = toolResult.ErrorMessage;
        }

        return viewModel;
    }

    private string GetExecutionSummary(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;
            
            var success = root.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
            var output = root.TryGetProperty("output", out var outputElement) ? outputElement.GetString() : "";
            var executionTime = root.TryGetProperty("executionTime", out var timeElement) ? timeElement.GetString() : "";
            
            var summary = success ? "? **Success**" : "? **Failed**";
            
            if (!string.IsNullOrEmpty(executionTime))
            {
                summary += $" (in {executionTime})";
            }
            
            if (!string.IsNullOrEmpty(output) && output.Length < 200)
            {
                summary += $"\n```\n{output}\n```";
            }
            else if (!string.IsNullOrEmpty(output))
            {
                summary += $"\n```\n{output[..197]}...\n```";
            }
            
            if (root.TryGetProperty("outputVariables", out var varsElement))
            {
                var varsJson = varsElement.GetRawText();
                if (varsJson != "{}" && varsJson != "null")
                {
                    summary += $"\n\n**Output Variables:** {varsJson}";
                }
            }
            
            return summary;
        }
        catch
        {
            return toolResult.Length > 200 ? toolResult[..197] + "..." : toolResult;
        }
    }
}