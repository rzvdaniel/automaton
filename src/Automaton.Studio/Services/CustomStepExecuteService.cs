using Automaton.Core.Logs;
using Automaton.Core.Scripting.Engines;
using Automaton.Core.Scripting.ErrorParsers;
using Automaton.Core.Scripting.Languages;
using Automaton.Core.Scripting.Step;
using Automaton.Studio.Domain;
using Automaton.Studio.Models;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;

namespace Automaton.Studio.Services;

public class CustomStepExecuteService : ICustomStepExecuteService, IDisposable
{
    private readonly IMediator _mediator;
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigurationService _configurationService;
    private readonly StudioFlowConvertService _flowConvertService;
    private readonly FlowExecutionsService _flowExecutionsService;
    private readonly ScriptEngineHost _scriptHost;
    private readonly CustomStepsService _customStepsService;
    private readonly ScriptEngineConfiguration _scriptConfig;
    private readonly IScriptLanguageProvider _languageProvider;

    public event EventHandler<string> ScriptTextWritten;

    public CustomStepExecuteService
    (
        StudioFlowConvertService flowConvertService,
        FlowExecutionsService flowExecutionsService,
        ConfigurationService configurationService,
        ScriptEngineHost scriptHost,
        IServiceProvider serviceProvider,
        IMediator mediator,
        CustomStepsService customStepsService,
        ScriptEngineConfiguration scriptConfig,
        IScriptLanguageProvider languageProvider)
    {
        _mediator = mediator;
        _configurationService = configurationService;
        _flowConvertService = flowConvertService;
        _flowExecutionsService = flowExecutionsService;
        _serviceProvider = serviceProvider;
        _scriptHost = scriptHost;
        _customStepsService = customStepsService;
        _scriptConfig = scriptConfig;
        _languageProvider = languageProvider ?? new DefaultScriptLanguageProvider();
        _scriptHost.ScriptTextWritten += OnScriptTextWritten;

        _logger = Log.ForContext<CustomStepExecuteService>();
    }

    /// <summary>
    /// Execute custom step locally (Desktop/Debug mode only) and return structured results
    /// </summary>
    public LocalCustomStepExecutionResult Execute(CustomStep step, CancellationToken cancellationToken = default)
    {
        using var customStepExecution = new CustomStepExecution();
        var consoleOutput = new StringBuilder();
        var started = DateTime.UtcNow;

        LogContext.PushProperty(LogContextProperties.StepId, step.Id);
        LogContext.PushProperty(LogContextProperties.StepName, step.Name);

        _logger.Information("Start step: {0}", step.Name);

        // Capture console output during execution
        EventHandler<string> consoleHandler = (sender, text) =>
        {
            consoleOutput.Append(text);
        };

        _scriptHost.ScriptTextWritten += consoleHandler;

        try
        {
            ExecuteAsync(step);

            var finished = DateTime.UtcNow;

            _logger.Information("End step: {0}", step.Name);

            return new LocalCustomStepExecutionResult
            {
                Success = true,
                ConsoleOutput = consoleOutput.ToString(),
                OutputVariables = step.OutputVariables?.ToDictionary(v => v.Name, v => (object)v.Value) ?? new Dictionary<string, object>(),
                Started = started,
                Finished = finished
            };
        }
        catch (Exception ex)
        {
            customStepExecution.HasErrors();

            _logger.Error(ex, "Step: {0} encountered an error. Message: {1}", step.Name, ex.Message);

            var finished = DateTime.UtcNow;

            return new LocalCustomStepExecutionResult
            {
                Success = false,
                ConsoleOutput = consoleOutput.ToString(),
                Error = ex.Message,
                OutputVariables = new Dictionary<string, object>(),
                Started = started,
                Finished = finished
            };
        }
        finally
        {
            _scriptHost.ScriptTextWritten -= consoleHandler;
        }
    }

    /// <summary>
    /// Execute custom step on specified runners
    /// </summary>
    public async Task<IEnumerable<RunnerCustomStepResult>> ExecuteOnRunners(Guid customStepId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken = default)
    {
        _logger.Information("Executing custom step {CustomStepId} on {RunnerCount} runners", customStepId, runnerIds.Count());

        try
        {
            var results = await _customStepsService.Execute(customStepId, runnerIds, cancellationToken);

            foreach (var result in results)
            {
                if (result.Success)
                {
                    _logger.Information("Custom step {CustomStepId} executed successfully on runner {RunnerId}",
                        customStepId, result.RunnerId);
                }
                else
                {
                    _logger.Warning("Custom step {CustomStepId} failed on runner {RunnerId}: {Error}",
                   customStepId, result.RunnerId, result.Error);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error executing custom step {CustomStepId} on runners", customStepId);
            throw;
        }
    }

    protected void ExecuteAsync(CustomStep step)
    {
        // ✅ UPDATED: Convert input variables to JSON strings
        var inputVariablesDictionary = step.InputVariables.ToDictionary(
            x => x.Name,
            x => JsonConvert.SerializeObject(x.Value));

        // Convert output variable definitions
        var outputVariableNames = step.OutputVariables?
            .Select(v => v.Name);

        // Use unified executor for consistency with Runner
        var executor = new CustomStepExecutor(_scriptHost);
        var result = executor.Execute(step.Code,
            step.Definition.Language.ToString().ToLowerInvariant(),
            inputVariablesDictionary,
            outputVariableNames);

        // Update output variables with execution results
        if (result.OutputVariables != null)
        {
            foreach (var kvp in result.OutputVariables)
            {
                var variable = step.OutputVariables.FirstOrDefault(v => v.Name == kvp.Key);
                if (variable != null)
                {
                    // ✅ UPDATED: Use value directly (already JSON string)
                    variable.Value = kvp.Value;
                }
            }
        }

        // Propagate any errors
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Script execution failed");
        }
    }

    private void OnScriptTextWritten(object sender, string e)
    {
        ScriptTextWritten?.Invoke(this, e);
    }

    public void Dispose()
    {
        _scriptHost.ScriptTextWritten -= OnScriptTextWritten;
    }

    /// <summary>
    /// Execute code asynchronously for MCP integration with unified error handling
    /// </summary>
    public async Task<CodeExecutionResult> ExecuteCustomStepCodeAsync
    (
        string code,
        string language,
        Dictionary<string, object> inputVariables,
        string stepId
    )
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            LogContext.PushProperty(LogContextProperties.StepId, stepId);
            _logger.Information("Executing {Language} code for step {StepId}", language, stepId);

            var contentType = _languageProvider.GetContentType(language);
            var resource = new ScriptResource()
            {
                ContentType = contentType,
                Content = code
            };

            // ✅ UPDATED: Convert input variables to JSON strings
            var stringInputs = inputVariables.ToDictionary(
                kvp => kvp.Key,
                kvp => JsonConvert.SerializeObject(kvp.Value));

            // Execute on a background thread to avoid blocking
            var scriptVariables = await Task.Run(() => _scriptHost.Execute(resource, stringInputs));

            stopwatch.Stop();

            // Use language-specific error parser for unified error handling
            var errorParser = ScriptErrorParserFactory.GetParser(language);

            // Extract outputs (stdout/stderr if captured)
            var standardOutput = scriptVariables.ContainsKey("stdout")
                ? scriptVariables["stdout"]?.ToString() ?? string.Empty
                : string.Empty;

            var standardError = scriptVariables.ContainsKey("stderr")
                ? scriptVariables["stderr"]?.ToString() ?? string.Empty
                : string.Empty;

            // Check for errors using parser
            var hasErrors = errorParser.HasErrors(standardOutput, standardError) ||
                scriptVariables.ContainsKey("error");

            ScriptErrorInfo? errorInfo = null;
            if (hasErrors)
            {
                // ✅ UPDATED: Convert string dictionary to object dictionary for error parser
                var scriptVariablesAsObject = scriptVariables.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)kvp.Value);
                errorInfo = errorParser.ParseError(standardOutput, standardError, scriptVariablesAsObject);

                _logger.Warning("Script execution failed: {ErrorType} - {Message}",
                    errorInfo?.ErrorType ?? "Unknown",
                    errorInfo?.Message ?? "Unknown error");
            }

            // Filter out internal variables from output
            var internalKeys = new[] { "error", "stackTrace", "errorOutput", "stdout", "stderr" };
            var cleanVariables = scriptVariables
                .Where(kvp => !internalKeys.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

            // Build error list from structured error info
            var errorsList = new List<string>();
            if (errorInfo != null)
            {
                // Format error for AI consumption
                errorsList.Add(errorInfo.GetFormattedErrorForAI());
            }

            // Build output message
            var outputMessage = hasErrors ? 
                $"**Execution Failed**\n\n{errorInfo?.Message ?? "Unknown error"}" : 
                standardOutput;

            return new CodeExecutionResult
            {
                Success = !hasErrors,
                Output = outputMessage,
                Errors = errorsList.ToArray(),
                OutputVariables = cleanVariables,
                ExecutionTime = $"{stopwatch.ElapsedMilliseconds}ms",
                ErrorInfo = errorInfo // Add structured error info
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(ex, "Error executing {Language} code for step {StepId}", language, stepId);

            // Create structured error from exception
            var errorInfo = new ScriptErrorInfo
            {
                ErrorType = ex.GetType().Name,
                Message = ex.Message,
                StackTrace = ex.StackTrace ?? string.Empty
            };

            return new CodeExecutionResult
            {
                Success = false,
                Output = $"**Execution Exception**\n\n{ex.Message}",
                Errors = new[] { errorInfo.GetFormattedErrorForAI() },
                OutputVariables = new Dictionary<string, object>(),
                ExecutionTime = $"{stopwatch.ElapsedMilliseconds}ms",
                ErrorInfo = errorInfo
            };
        }
    }
}

/// <summary>
/// Result of local custom step execution
/// </summary>
public class LocalCustomStepExecutionResult
{
    public bool Success { get; set; }
    public string ConsoleOutput { get; set; } = string.Empty;
    public string? Error { get; set; }
    public Dictionary<string, object> OutputVariables { get; set; } = new();
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
}
