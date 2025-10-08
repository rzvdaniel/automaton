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
            Icon = "experiment",
            Description = "Testing code execution",
            StatusIcon = toolResult.IsSuccess ? "check-circle" : "close-circle",
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
            // Extract detailed error information from the result
            viewModel.ErrorMessage = ExtractErrorMessage(toolResult);
            
            // Also set raw result to full details if available
            if (!string.IsNullOrEmpty(toolResult.Result))
            {
                viewModel.RawResult = toolResult.Result;
            }
        }

        return viewModel;
    }

    private string ExtractErrorMessage(ToolResult toolResult)
    {
        // Try to parse the result as JSON and extract detailed error info
        if (!string.IsNullOrEmpty(toolResult.Result))
        {
            try
            {
                // Check if result contains "Detailed Result:" which indicates error details
                if (toolResult.Result.Contains("Detailed Result:"))
                {
                    var parts = toolResult.Result.Split(new[] { "Detailed Result:" }, StringSplitOptions.None);
                    if (parts.Length > 1)
                    {
                        var jsonPart = parts[1].Trim();
                        using var doc = JsonDocument.Parse(jsonPart);
                        var root = doc.RootElement;
                        
                        // Extract error information
                        var errorMessage = new System.Text.StringBuilder();
                        
                        // Add the main error message
                        if (root.TryGetProperty("errorInfo", out var errorInfo) && errorInfo.ValueKind != JsonValueKind.Null)
                        {
                            if (errorInfo.TryGetProperty("formattedError", out var formattedError))
                            {
                                errorMessage.AppendLine(formattedError.GetString());
                            }
                            else if (errorInfo.TryGetProperty("message", out var message))
                            {
                                errorMessage.AppendLine(message.GetString());
                                
                                if (errorInfo.TryGetProperty("lineNumber", out var lineNumber) && lineNumber.ValueKind != JsonValueKind.Null)
                                {
                                    errorMessage.AppendLine($"Line: {lineNumber.GetInt32()}");
                                }
                            }
                        }
                        else if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var error in errors.EnumerateArray())
                            {
                                errorMessage.AppendLine(error.GetString());
                            }
                        }
                        else if (root.TryGetProperty("output", out var output))
                        {
                            errorMessage.AppendLine(output.GetString());
                        }
                        
                        if (errorMessage.Length > 0)
                        {
                            return errorMessage.ToString().Trim();
                        }
                    }
                }
            }
            catch
            {
                // If parsing fails, fall through to default handling
            }
        }
        
        // Fallback to error message or raw result
        return toolResult.ErrorMessage ?? toolResult.Result ?? "Unknown error occurred";
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
            
            var summary = success ? "**Success**" : "**Failed**";
            
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
                    summary += varsJson;
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