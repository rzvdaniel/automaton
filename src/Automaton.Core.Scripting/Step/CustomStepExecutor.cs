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
        Dictionary<string, string> inputVariables,
        IEnumerable<string>? outputVariableNames = null
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
            var outputVars = ExtractOutputVariables(outputVariableNames, scriptVariables);

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

    private IDictionary<string, string> ExecuteScript
    (
        string code,
        string language,
        Dictionary<string, string> inputVariables
    )
    {
        var contentType = _languageProvider.GetContentType(language);
        var resource = new ScriptResource
        {
            ContentType = contentType,
            Content = code
        };

        var execution = _scriptHost.Execute(resource, inputVariables);
        if (!execution.Success)
            throw new InvalidOperationException(execution.Error?.Message ?? "Custom step script execution failed.");

        return execution.OutputVariables.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private Dictionary<string, string> ExtractOutputVariables
    (
        IEnumerable<string>? outputVariableNames,
        IDictionary<string, string> scriptVariables
    )
    {
        if (outputVariableNames == null || !outputVariableNames.Any())
        {
            return new Dictionary<string, string>();
        }

        // Output variables are already JSON strings from ExecuteScript
        // Just pass them through directly
        return outputVariableNames
            .ToDictionary(
                outputVar => outputVar,
                outputVar => scriptVariables.ContainsKey(outputVar) ? 
                (scriptVariables[outputVar] ?? string.Empty) : 
                string.Empty);
    }

    private CustomStepExecutionResult CreateSuccessResult
    (
        string output,
        DateTime started,
        Stopwatch stopwatch,
        Dictionary<string, string> outputVars
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
        Dictionary<string, string> outputVars
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
            OutputVariables = new Dictionary<string, string>()
        };
    }
}
