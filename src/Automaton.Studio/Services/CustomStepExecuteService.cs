using AutoMapper;
using Automaton.Core.Logs;
using Automaton.Core.Scripting;
using Automaton.Core.Scripting.ErrorParsers;
using Automaton.Studio.Domain;
using System.Diagnostics;

namespace Automaton.Studio.Services;

public class CustomStepExecuteService : ICustomStepExecuteService, IDisposable
{
    private readonly IMediator mediator;
    private readonly IMapper mapper;
    private readonly ILogger logger;
    private readonly IServiceProvider serviceProvider;
    private readonly ConfigurationService configurationService;
    private readonly StudioFlowConvertService flowConvertService;
    private readonly FlowExecutionsService flowExecutionsService;
    private readonly ScriptEngineHost scriptHost;

    public event EventHandler<string> ScriptTextWritten;

    public CustomStepExecuteService
    (
        StudioFlowConvertService flowConvertService, 
        FlowExecutionsService flowExecutionsService,
        ConfigurationService configurationService,
        ScriptEngineHost scriptHost,
        IServiceProvider serviceProvider,
        IMediator mediator, 
        IMapper mapper
    ) 
    {
        this.mediator = mediator;
        this.mapper = mapper;
        this.configurationService = configurationService;
        this.flowConvertService = flowConvertService;
        this.flowExecutionsService = flowExecutionsService;
        this.serviceProvider = serviceProvider;
        this.scriptHost = scriptHost;
        this.scriptHost.ScriptTextWritten += OnScriptTextWritten;

        logger = Log.ForContext<StudioFlowExecuteService>();
    }

    public CustomStepExecution Execute(CustomStep step, CancellationToken cancellationToken = default)
    {
        using var customStepExecution = new CustomStepExecution();

        LogContext.PushProperty(LogContextProperties.StepId, step.Id);
        LogContext.PushProperty(LogContextProperties.StepName, step.Name);

        logger.Information("Start step: {0}", step.Name);

        try
        {
            ExecuteAsync(step);
        }
        catch (Exception ex)
        {
            customStepExecution.HasErrors();

            logger.Error(ex, "Step: {0} encountered an error. Message: {1}", step.Name, ex.Message);

            throw;
        }

        logger.Information("End step: {0}", step.Name);

        return customStepExecution;
    }

    protected void ExecuteAsync(CustomStep step)
    {
        var contentType = step.Definition.Language switch
        {
            CustomStepLanguage.Python => "text/x-python",
            CustomStepLanguage.Batch => "text/x-batch",
            _ => "text/x-python"
        };

        var resource = new ScriptResource()
        {
            ContentType = contentType,
            Content = step.Code
        };

        var inputVariablesDictionary = step.InputVariables.ToDictionary(x => x.Name, x => x.Value);

        var scriptVariables = scriptHost.Execute(resource, inputVariablesDictionary);

        foreach (var variable in step.OutputVariables)
        {
            if (scriptVariables.ContainsKey(variable.Name))
            {
                variable.Value = scriptVariables[variable.Name].ToString();
            }
        }
    }

    private void OnScriptTextWritten(object sender, string e)
    {
        ScriptTextWritten?.Invoke(this, e);
    }

    public void Dispose()
    {
        scriptHost.ScriptTextWritten -= OnScriptTextWritten;
    }

    /// <summary>
    /// Execute code asynchronously for MCP integration with unified error handling
    /// </summary>
    public async Task<CodeExecutionResult> ExecuteCustomStepCodeAsync(
        string code, 
        string language, 
        Dictionary<string, object> inputVariables, 
        string stepId)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            LogContext.PushProperty(LogContextProperties.StepId, stepId);
            logger.Information("Executing {Language} code for step {StepId}", language, stepId);

            var contentType = language.ToLowerInvariant() switch
            {
                "python" => "text/x-python",
                "batch" => "text/x-batch",
                "javascript" => "text/javascript",
                "powershell" => "text/x-powershell",
                _ => "text/x-python"
            };

            var resource = new ScriptResource()
            {
                ContentType = contentType,
                Content = code
            };

            // Execute on a background thread to avoid blocking
            var scriptVariables = await Task.Run(() => scriptHost.Execute(resource, inputVariables));
            
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
                errorInfo = errorParser.ParseError(standardOutput, standardError, scriptVariables);
                
                logger.Warning("Script execution failed: {ErrorType} - {Message}", 
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
            var outputMessage = hasErrors 
                ? $"**Execution Failed**\n\n{errorInfo?.Message ?? "Unknown error"}" 
                : standardOutput;

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
            logger.Error(ex, "Error executing {Language} code for step {StepId}", language, stepId);
            
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
