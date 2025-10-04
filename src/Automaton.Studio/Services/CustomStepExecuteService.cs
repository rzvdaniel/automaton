using AutoMapper;
using Automaton.Core.Logs;
using Automaton.Core.Scripting;
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
    /// Execute code asynchronously for MCP integration
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

            // Check if execution had errors by looking for error variables
            var hasErrors = scriptVariables?.ContainsKey("error") == true;
            var errorMessage = hasErrors ? scriptVariables["error"]?.ToString() : null;
            var stackTrace = scriptVariables?.ContainsKey("stackTrace") == true ? scriptVariables["stackTrace"]?.ToString() : null;

            // Filter out error-related variables from output
            var cleanVariables = scriptVariables?.Where(kvp => 
                kvp.Key != "error" && 
                kvp.Key != "stackTrace")
                .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value) ?? new Dictionary<string, object>();

            var errorsList = new List<string>();
            if (hasErrors)
            {
                if (!string.IsNullOrEmpty(errorMessage))
                    errorsList.Add(errorMessage);
                if (!string.IsNullOrEmpty(stackTrace))
                    errorsList.Add(stackTrace);
            }

            return new CodeExecutionResult
            {
                Success = !hasErrors,
                Output = hasErrors ? $"{errorMessage}" : "",
                Errors = errorsList.ToArray(),
                OutputVariables = cleanVariables,
                ExecutionTime = $"{stopwatch.ElapsedMilliseconds}ms"
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.Error(ex, "Error executing {Language} code for step {StepId}", language, stepId);
            
            return new CodeExecutionResult
            {
                Success = false,
                Output = "",
                Errors = new[] { ex.Message },
                OutputVariables = new Dictionary<string, object>(),
                ExecutionTime = $"{stopwatch.ElapsedMilliseconds}ms"
            };
        }
    }
}
