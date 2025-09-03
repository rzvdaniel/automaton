using Automaton.Core.Models;
using Automaton.Core.Enums;
using Serilog;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class FlowExecutionService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ConsoleAuthenticationService authService;
    private readonly ILogger logger;

    public FlowExecutionService(
        HttpClient httpClient,
        ConfigurationService configService,
        ConsoleAuthenticationService authService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.authService = authService;
        this.logger = Log.ForContext<FlowExecutionService>();
    }

    public async Task<WorkflowExecution> ExecuteWorkflowAsync(Guid workflowId)
    {
        var execution = new WorkflowExecution(workflowId, configService.ApplicationName);
        
        try
        {
            logger.Information("Starting execution of workflow {WorkflowId}", workflowId);
            
            // Get the workflow definition
            var workflow = await GetWorkflowAsync(workflowId);
            if (workflow == null)
            {
                throw new InvalidOperationException($"Workflow {workflowId} not found");
            }

            // For now, create a simple execution result
            // In a full implementation, you would use the FlowExecuteService here
            execution.Started = DateTime.UtcNow;
            execution.Status = WorkflowStatus.Working;
            
            // Simulate workflow execution
            await Task.Delay(1000); // Simulate some work
            
            execution.Finished = DateTime.UtcNow;
            execution.Status = WorkflowStatus.Success;
            
            logger.Information("Completed execution of workflow {WorkflowId} with status {Status}", 
                workflowId, execution.Status);

            // Report execution result back to server
            await ReportExecutionAsync(execution);

            return execution;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to execute workflow {WorkflowId}", workflowId);
            execution.Finished = DateTime.UtcNow;
            execution.Status = WorkflowStatus.Error;
            await ReportExecutionAsync(execution); // Report the failure too
            throw;
        }
    }

    private async Task<Workflow?> GetWorkflowAsync(Guid workflowId)
    {
        try
        {
            var accessToken = await authService.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new UnauthorizedAccessException("No valid access token available");
            }

            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.GetAsync($"{configService.BaseUrl}/{configService.FlowsUrl}/{workflowId}");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var workflow = JsonSerializer.Deserialize<Workflow>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                return workflow;
            }

            logger.Warning("Failed to get workflow {WorkflowId}: {StatusCode}", workflowId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error getting workflow {WorkflowId}", workflowId);
            return null;
        }
    }

    private async Task ReportExecutionAsync(WorkflowExecution execution)
    {
        try
        {
            var accessToken = await authService.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                logger.Warning("Cannot report execution - no valid access token");
                return;
            }

            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var executionData = new
            {
                execution.Id,
                execution.FlowId,
                execution.Started,
                execution.Finished,
                Status = execution.Status.ToString(),
                execution.Application
            };

            var json = JsonSerializer.Serialize(executionData);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/{configService.FlowExecutionUrl}", content);
            
            if (response.IsSuccessStatusCode)
            {
                logger.Information("Execution result reported successfully for workflow {WorkflowId}", execution.FlowId);
            }
            else
            {
                logger.Warning("Failed to report execution result for workflow {WorkflowId}: {StatusCode}", 
                    execution.FlowId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error reporting execution for workflow {WorkflowId}", execution.FlowId);
        }
    }
}