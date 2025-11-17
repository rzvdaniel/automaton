using Automaton.Mcp.Models;
using Automaton.Studio.Pages.Steps.Designer.Components.ToolResults;
using System.Text.Json;

namespace Automaton.Studio.Pages.Steps.Designer.Components.ToolResults.Processors;

public class GetStepVariablesProcessor : IToolResultProcessor
{
    public string ToolName => "get_step_variables";

    public ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage)
    {
        var viewModel = new ToolResultViewModel
        {
            Icon = "search",
            Description = "Analyzing step variables",
            StatusIcon = toolResult.IsSuccess ? "check-circle" : "close-circle",
            Status = toolResult.IsSuccess ? "Success" : "Failed",
            IsSuccess = toolResult.IsSuccess,
            ResultType = ToolResultType.Variables
        };

        if (toolResult.IsSuccess && !string.IsNullOrEmpty(toolResult.Result))
        {
            viewModel.Summary = GetVariableSummary(toolResult.Result);
            viewModel.RawResult = toolResult.Result;
        }
        else if (!toolResult.IsSuccess)
        {
            viewModel.ErrorMessage = toolResult.ErrorMessage;
        }

        return viewModel;
    }

    private string GetVariableSummary(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            var root = doc.RootElement;
            var summary = "";
            
            if (root.TryGetProperty("inputVariables", out var inputVars) && inputVars.ValueKind == JsonValueKind.Array)
            {
                var inputCount = inputVars.GetArrayLength();
                summary += $"**{inputCount} Input Variable{(inputCount != 1 ? "s" : "")}**";
                
                foreach (var variable in inputVars.EnumerateArray().Take(3))
                {
                    if (variable.TryGetProperty("name", out var nameEl) && 
                        variable.TryGetProperty("type", out var typeEl))
                    {
                        summary += $"\n• `{nameEl.GetString()}` ({typeEl.GetString()})";
                    }
                }
                
                if (inputCount > 3)
                {
                    summary += $"\n• ... and {inputCount - 3} more";
                }
            }
            
            if (root.TryGetProperty("outputVariables", out var outputVars) && outputVars.ValueKind == JsonValueKind.Array)
            {
                var outputCount = outputVars.GetArrayLength();
                if (!string.IsNullOrEmpty(summary)) summary += "\n\n";
                summary += $"**{outputCount} Output Variable{(outputCount != 1 ? "s" : "")}**";
                
                foreach (var variable in outputVars.EnumerateArray().Take(3))
                {
                    if (variable.TryGetProperty("name", out var nameEl) && 
                        variable.TryGetProperty("type", out var typeEl))
                    {
                        summary += $"\n• `{nameEl.GetString()}` ({typeEl.GetString()})";
                    }
                }
                
                if (outputCount > 3)
                {
                    summary += $"\n• ... and {outputCount - 3} more";
                }
            }
            
            return summary;
        }
        catch
        {
            return "Variables analyzed";
        }
    }
}