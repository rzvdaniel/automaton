using Automaton.Mcp.Client.Models;
using System.Text.Json;
using Automaton.Studio.Pages.Steps.Designer.Components.ToolResults;
using Automaton.Studio.Pages.Steps.Designer.Components.ToolResults.Utilities;

namespace Automaton.Studio.Pages.Steps.Designer.Components.ToolResults.Processors;

public class GetCurrentCodeProcessor : IToolResultProcessor
{
    public string ToolName => "get_current_code";

    public ToolResultViewModel ProcessResult(ToolResult toolResult, string currentLanguage)
    {
        var viewModel = new ToolResultViewModel
        {
            Icon = "file-text",
            Description = "Reading current code from editor",
            StatusIcon = toolResult.IsSuccess ? "check-circle" : "close-circle",
            Status = toolResult.IsSuccess ? "Success" : "Failed",
            IsSuccess = toolResult.IsSuccess,
            ResultType = ToolResultType.Code,
            Language = currentLanguage
        };

        if (toolResult.IsSuccess && !string.IsNullOrEmpty(toolResult.Result))
        {
            var fullCode = CodeExtractionUtility.ExtractCodeFromResult(toolResult.Result);
            if (!string.IsNullOrEmpty(fullCode))
            {
                viewModel.CodeContent = fullCode;
                viewModel.LineCount = CodeExtractionUtility.GetCodeLineCount(fullCode);
                viewModel.RawResult = CreateCodeResultData(fullCode, currentLanguage);
            }
        }
        else if (!toolResult.IsSuccess)
        {
            viewModel.ErrorMessage = toolResult.ErrorMessage;
        }

        return viewModel;
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
                linesOfCode = CodeExtractionUtility.GetCodeLineCount(code),
                hasErrorHandling = code.Contains("try") || code.Contains("except"),
                hasLogging = code.Contains("print") || code.Contains("log"),
                isStepTemplate = false
            }
        };

        return JsonSerializer.Serialize(codeResultData, new JsonSerializerOptions { WriteIndented = true });
    }
}