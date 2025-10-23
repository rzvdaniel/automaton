using Automaton.Core.Scripting.Engines;
using Automaton.Core.Scripting.ErrorDetection;
using Automaton.Core.Scripting.Languages;
using System.Diagnostics;
using System.Text;

namespace Automaton.Core.Scripting.Step;

/// <summary>
/// Unified service for executing custom steps with consistent error handling across Runner.Console and Studio.
/// </summary>
public class CustomStepExecutor
{
    private readonly IScriptEngineHost _scriptHost;
    private readonly IScriptLanguageProvider _languageProvider;
    private readonly IScriptErrorDetector _errorDetector;

    public CustomStepExecutor(
        IScriptEngineHost scriptHost,
        IScriptLanguageProvider? languageProvider = null,
        IScriptErrorDetector? errorDetector = null)
    {
        _scriptHost = scriptHost ?? throw new ArgumentNullException(nameof(scriptHost));
        _languageProvider = languageProvider ?? new DefaultScriptLanguageProvider();
        _errorDetector = errorDetector ?? new DefaultScriptErrorDetector();
    }

    /// <summary>
    /// Execute a custom step with unified error handling and output variable extraction.
    /// Used by both Runner.Console and Studio to ensure consistent behavior.
    /// </summary>
    public CustomStepExecutionResult Execute
    (
        string code,
        string language,
        Dictionary<string, object> inputVariables,
        List<StepOutputVariableDefinition>? outputVariableDefinitions = null
    )
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code cannot be null or empty", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("Language cannot be null or empty", nameof(language));
        }

        var stopwatch = Stopwatch.StartNew();
        var started = DateTime.UtcNow;
        var consoleOutput = new StringBuilder();
        EventHandler<string>? outputHandler = null;

        try
        {
            // Setup output capture
            outputHandler = (_, text) => consoleOutput.Append(text);
            _scriptHost.ScriptTextWritten += outputHandler;

            // Execute script
            var scriptVariables = ExecuteScript(code, language, inputVariables);
            stopwatch.Stop();

            // Extract output variables
            var outputVars = ExtractOutputVariables(outputVariableDefinitions, scriptVariables);

            // Detect errors using strategy pattern
            var executionError = _errorDetector.DetectError(scriptVariables, consoleOutput.ToString());

            if (executionError != null)
            {
                return CreateErrorResult(executionError, consoleOutput.ToString(), started, stopwatch, outputVars);
            }

            return CreateSuccessResult(consoleOutput.ToString(), started, stopwatch, outputVars);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CreateExceptionResult(ex, consoleOutput.ToString(), started, stopwatch);
        }
        finally
        {
            if (outputHandler != null)
            {
                _scriptHost.ScriptTextWritten -= outputHandler;
            }
        }
    }

    private IDictionary<string, dynamic> ExecuteScript
    (
        string code,
        string language,
        Dictionary<string, object> inputVariables
    )
    {
        var contentType = _languageProvider.GetContentType(language);
        var resource = new ScriptResource
        {
            ContentType = contentType,
            Content = code
        };

        return _scriptHost.Execute(resource, inputVariables);
    }

    private Dictionary<string, object> ExtractOutputVariables
    (
        List<StepOutputVariableDefinition>? outputVariableDefinitions,
        IDictionary<string, dynamic> scriptVariables
    )
    {
        if (outputVariableDefinitions == null || !outputVariableDefinitions.Any())
        {
            return new Dictionary<string, object>();
        }

        return outputVariableDefinitions
            .ToDictionary(
                outputVar => outputVar.Name,
                outputVar => scriptVariables.ContainsKey(outputVar.Name) ? 
                (object)(scriptVariables[outputVar.Name]?.ToString() ?? string.Empty) : 
                string.Empty);
    }

    private CustomStepExecutionResult CreateSuccessResult
    (
        string output,
        DateTime started,
        Stopwatch stopwatch,
        Dictionary<string, object> outputVars
    )
    {
        var displayOutput = !string.IsNullOrEmpty(output) ? output : "Custom step executed successfully";

        return new CustomStepExecutionResult
        {
            Success = true,
            Output = displayOutput,
            Error = null,
            Started = started,
            Finished = DateTime.UtcNow,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            OutputVariables = outputVars
        };
    }

    private CustomStepExecutionResult CreateErrorResult
    (
        string errorMessage,
        string output,
        DateTime started,
        Stopwatch stopwatch,
        Dictionary<string, object> outputVars
    )
    {
        return new CustomStepExecutionResult
        {
            Success = false,
            Output = output,
            Error = errorMessage,
            Started = started,
            Finished = DateTime.UtcNow,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            OutputVariables = outputVars
        };
    }

    private CustomStepExecutionResult CreateExceptionResult
    (
        Exception ex,
        string output,
        DateTime started,
        Stopwatch stopwatch
    )
    {
        return new CustomStepExecutionResult
        {
            Success = false,
            Output = output,
            Error = $"Execution exception: {ex.Message}",
            Started = started,
            Finished = DateTime.UtcNow,
            ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
            OutputVariables = new Dictionary<string, object>()
        };
    }
}