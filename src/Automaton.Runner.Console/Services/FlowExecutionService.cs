using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Core.Enums;
using Serilog;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class FlowExecutionService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly FlowConvertService flowConvertService;
    private readonly FlowExecuteService flowExecuteService;
    private readonly ILogger logger;

    public FlowExecutionService(
        HttpClient httpClient,
        ConfigurationService configService,
        ApiKeyAuthenticationService apiKeyAuthService,
        FlowConvertService flowConvertService,
        FlowExecuteService flowExecuteService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.flowConvertService = flowConvertService;
        this.flowExecuteService = flowExecuteService;
        this.logger = Log.ForContext<FlowExecutionService>();
    }

    public async Task<WorkflowExecution> ExecuteWorkflowAsync(Guid workflowId)
    {        
        try
        {
            logger.Information("Starting execution of workflow {WorkflowId}", workflowId);
            
            // Get the flow definition from server and convert to workflow
            var flow = await LoadFlowAsync(workflowId);
            if (flow == null)
            {
                throw new InvalidOperationException($"Workflow {workflowId} not found");
            }

            // Execute the workflow using the same pattern as the old runner
            var workflowExecution = await ExecuteAsync(flow);
            
            logger.Information("Completed execution of workflow {WorkflowId} with status {Status}", 
                workflowId, workflowExecution.Status);

            // Save the execution result back to server
            await SaveWorkflowExecutionAsync(workflowExecution);

            return workflowExecution;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to execute workflow {WorkflowId}", workflowId);
            
            // Create error execution result
            var errorExecution = new WorkflowExecution(workflowId, configService.ApplicationName)
            {
                Started = DateTime.UtcNow,
                Finished = DateTime.UtcNow,
                Status = WorkflowStatus.Error
            };
            
            await SaveWorkflowExecutionAsync(errorExecution); // Report the failure too
            throw;
        }
    }

    private async Task<WorkflowExecution> ExecuteAsync(Flow flow, CancellationToken cancellationToken = default)
    {
        // Convert Flow to Workflow (same as old runner)
        var workflow = flowConvertService.ConvertFlow(flow);

        // Execute the workflow using FlowExecuteService (same as old runner) 
        var workflowExecution = await flowExecuteService.Execute(workflow, cancellationToken);

        return workflowExecution;
    }

    private async Task<Flow?> LoadFlowAsync(Guid workflowId)
    {
        try
        {
            await SetupHttpClientAuthAsync();

            var response = await httpClient.GetAsync($"{configService.BaseUrl}/{configService.FlowsUrl}/{workflowId}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                
                // Deserialize as Flow first (what the server actually returns)
                var flow = JsonSerializer.Deserialize<Flow>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (flow != null)
                {
                    logger.Information("Successfully loaded flow {FlowId} from server", workflowId);
                    return flow;
                }
            }

            logger.Warning("Failed to load flow {WorkflowId}: {StatusCode}", workflowId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error loading flow {WorkflowId}", workflowId);
            return null;
        }
    }

    private async Task SaveWorkflowExecutionAsync(WorkflowExecution workflowExecution)
    {
        try
        {
            await SetupHttpClientAuthAsync();

            // Convert WorkflowExecution to the format expected by server (same as old runner)
            var flowExecution = new
            {
                Id = workflowExecution.Id,
                FlowId = workflowExecution.FlowId,
                Started = workflowExecution.Started,
                Finished = workflowExecution.Finished,
                Status = workflowExecution.Status.ToString(),
                Application = workflowExecution.Application
            };

            var json = JsonSerializer.Serialize(flowExecution);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/{configService.FlowExecutionUrl}", content);
            
            if (response.IsSuccessStatusCode)
            {
                logger.Information("Execution result saved successfully for workflow {WorkflowId}", workflowExecution.FlowId);
            }
            else
            {
                logger.Warning("Failed to save execution result for workflow {WorkflowId}: {StatusCode}", 
                    workflowExecution.FlowId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error saving execution result for workflow {WorkflowId}", workflowExecution.FlowId);
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