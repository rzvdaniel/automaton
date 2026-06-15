using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Serilog;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Runner persistence service that saves execution results via HTTP API calls
/// </summary>
public class RunnerWorkflowExecutionPersistenceService : IWorkflowExecutionPersistenceService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly ILogger logger;

    public RunnerWorkflowExecutionPersistenceService(
        HttpClient httpClient,
        ConfigurationService configService,
        ApiKeyAuthenticationService apiKeyAuthService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.logger = Log.ForContext<RunnerWorkflowExecutionPersistenceService>();
    }

    public async Task SaveWorkflowExecutionAsync(WorkflowExecution workflowExecution)
    {
        try
        {
            await SetupHttpClientAuthAsync();

            var flowExecution = new
            {
                Id = workflowExecution.Id,
                FlowId = workflowExecution.FlowId,
                Started = workflowExecution.Started,
                Finished = workflowExecution.Finished,
                Status = workflowExecution.Status.ToString(),
                Application = workflowExecution.Application,
                Logs = workflowExecution.Logs.Select(log => new
                {
                    Timestamp = log.Timestamp,
                    Level = log.Level,
                    Message = log.Message,
                    StepId = log.StepId,
                    StepName = log.StepName,
                    Exception = log.Exception
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(flowExecution);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/{configService.FlowExecutionUrl}", content);
            
            if (response.IsSuccessStatusCode)
            {
                logger.Debug("Successfully saved workflow execution {ExecutionId} via HTTP API with {LogCount} log entries", 
                    workflowExecution.Id, workflowExecution.Logs.Count);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to save execution result: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error saving workflow execution {ExecutionId} via HTTP API", workflowExecution.Id);
            throw;
        }
    }

    private async Task SetupHttpClientAuthAsync()
    {
        httpClient.DefaultRequestHeaders.Clear();
        
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();

        if (!string.IsNullOrEmpty(apiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            return;
        }

        throw new UnauthorizedAccessException("No valid API key available for authentication");
    }
}
