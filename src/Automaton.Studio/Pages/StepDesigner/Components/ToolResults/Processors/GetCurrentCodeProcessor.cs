using Automaton.Mcp.Client.Models;
using System.Text.Json;

namespace Automaton.Studio.Pages.StepDesigner.Components.ToolResults.Processors;

public class GetCurrentCodeProcessor : IToolResultProcessor
{
    public string ToolName => "get_current_code";

    public ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage)
    {
        var viewModel = new ToolResultViewModel
        {
            Icon = "??",
            Description = "Reading current code from editor",
            StatusIcon = toolResult.IsSuccess ? "?" : "?",
            Status = toolResult.IsSuccess ? "Success" : "Failed",
            IsSuccess = toolResult.IsSuccess,
            ResultType = ToolResultType.Code,
            Language = currentLanguage
        };

        if (toolResult.IsSuccess && !string.IsNullOrEmpty(toolResult.Result))
        {
            var fullCode = ExtractCodeFromResult(toolResult.Result);
            if (!string.IsNullOrEmpty(fullCode))
            {
                viewModel.CodeContent = fullCode;
                viewModel.LineCount = GetCodeLineCount(fullCode);
                viewModel.RawResult = CreateCodeResultData(fullCode, currentLanguage);
            }
        }
        else if (!toolResult.IsSuccess)
        {
            viewModel.ErrorMessage = toolResult.ErrorMessage;
        }

        return viewModel;
    }

    private string ExtractCodeFromResult(string toolResult)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            if (doc.RootElement.TryGetProperty("code", out var codeElement))
            {
                return codeElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
            if (toolResult.Contains("```"))
            {
                var start = toolResult.IndexOf("```");
                var end = toolResult.IndexOf("```", start + 3);
                if (end > start)
                {
                    var codeBlock = toolResult.Substring(start + 3, end - start - 3);
                    var firstNewline = codeBlock.IndexOf('\n');
                    if (firstNewline > 0 && firstNewline < 20)
                    {
                        codeBlock = codeBlock.Substring(firstNewline + 1);
                    }
                    return codeBlock.Trim();
                }
            }
        }
        
        return string.Empty;
    }

    private int GetCodeLineCount(string code)
    {
        if (string.IsNullOrEmpty(code)) return 0;
        return code.Split('\n', StringSplitOptions.None).Length;
    }

    private string CreateCodeResultData(string code, string language)
    {
        var codeResultData = new
        {
            stepId = "example-step-id",
            language = language,
            code = code,
            codeAnalysis = new
            {
                linesOfCode = GetCodeLineCount(code),
                hasErrorHandling = code.Contains("try") || code.Contains("except"),
                hasLogging = code.Contains("print") || code.Contains("log"),
                isStepTemplate = false
            }
        };

        return JsonSerializer.Serialize(codeResultData, new JsonSerializerOptions { WriteIndented = true });
    }
}