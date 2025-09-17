using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

public class FlowExecutionsService
{
    private HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly ILogger logger;

    public FlowExecutionsService
    (
        ConfigurationService configService,
        HttpClient httpClient
    )
    {
        this.logger = Log.ForContext<FlowExecutionsService>(); ;
        this.configService = configService;
        this.httpClient = httpClient;
    }

    public async Task<FlowExecutionResult> GetFlowExecutionResult(Guid flowId, int startIndex, int pageSize)
    {
        try
        {
            var result = await httpClient.GetAsync($"{configService.FlowExecutionUrl}/flow/{flowId}/{startIndex}/{pageSize}");

            result.EnsureSuccessStatusCode();

            var flowExecutionResult = await result.Content.ReadAsAsync<FlowExecutionResult>();

            return flowExecutionResult;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to load flow execution list");

            throw;
        }
    }

    public async Task<IEnumerable<FlowExecutionLogEntry>> GetLogsActivity(Guid flowId, Guid flowExecutionId)
    {
        try
        {
            var response = await httpClient.GetAsync($"{configService.FlowExecutionUrl}/logs/{flowExecutionId}");

            response.EnsureSuccessStatusCode();

            var logs = await response.Content.ReadAsAsync<IEnumerable<FlowExecutionLogEntry>>();

            return logs;

        }
        catch (Exception ex)
        {
            logger
                .ForContext("FlowId", flowId)
                .ForContext("FlowExecutionId", flowExecutionId)
                .Error(ex, "Failed to load flow execution logs");

            throw;
        }
    }

    public async Task<string> GetLogsActivityText(Guid flowId, Guid flowExecutionId)
    {
        try
        {
            var response = await httpClient.GetAsync($"{configService.FlowExecutionUrl}/logs/{flowExecutionId}/text");

            response.EnsureSuccessStatusCode();

            var logsText = await response.Content.ReadAsStringAsync();

            return logsText;
        }
        catch (Exception ex)
        {
            logger
                .ForContext("FlowId", flowId)
                .ForContext("FlowExecutionId", flowExecutionId)
                .Error(ex, "Failed to load flow execution logs text");

            throw;
        }
    }

    public async Task<string> GetLogsActivityJson(Guid flowId, Guid flowExecutionId)
    {
        try
        {
            var response = await httpClient.GetAsync($"{configService.FlowExecutionUrl}/logs/{flowExecutionId}/json");

            response.EnsureSuccessStatusCode();

            var logsJson = await response.Content.ReadAsStringAsync();

            return logsJson;
        }
        catch (Exception ex)
        {
            logger
                .ForContext("FlowId", flowId)
                .ForContext("FlowExecutionId", flowExecutionId)
                .Error(ex, "Failed to load flow execution logs JSON");

            throw;
        }
    }

    public async Task Add(FlowExecution flowExecution)
    {
        try
        {
            var flowExecutionModel = new 
            {
                flowExecution.Id,
                flowExecution.FlowId,
                flowExecution.Started,
                flowExecution.Finished,
                Status = flowExecution.Status.ToString(),
                flowExecution.Application,
                Logs = flowExecution.Logs?.Select(log => new
                {
                    log.Timestamp,
                    log.Level,
                    log.Message,
                    // Only include optional properties if they have meaningful values
                    StepId = string.IsNullOrEmpty(log.StepId) ? null : log.StepId,
                    StepName = string.IsNullOrEmpty(log.StepName) ? null : log.StepName,
                    Exception = string.IsNullOrEmpty(log.Exception) ? null : log.Exception
                }).ToArray() ?? Array.Empty<object>() // Ensure Logs is never null
            };

            logger.Debug("Sending flow execution to server: {ExecutionId} for Flow {FlowId} with {LogCount} log entries", 
                flowExecution.Id, flowExecution.FlowId, flowExecution.Logs?.Count ?? 0);

            // Log the first few log entries for debugging validation issues
            if (flowExecution.Logs?.Any() == true)
            {
                var firstLog = flowExecution.Logs.First();
                logger.Debug("First log entry - StepId: '{StepId}', StepName: '{StepName}', Exception: '{Exception}'", 
                    firstLog.StepId ?? "NULL", firstLog.StepName ?? "NULL", firstLog.Exception ?? "NULL");
            }

            var response = await httpClient.PostAsJsonAsync(configService.FlowExecutionUrl, flowExecutionModel);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.Error("Failed to save flow execution {ExecutionId}. Server responded with {StatusCode}: {ErrorContent}", 
                    flowExecution.Id, response.StatusCode, errorContent);
                    
                // Create a more informative exception
                throw new HttpRequestException($"Failed to save flow execution. Server returned {response.StatusCode}: {errorContent}", 
                    null, response.StatusCode);
            }
            
            logger.Information("Successfully saved flow execution {ExecutionId} to server", flowExecution.Id);
        }
        catch (HttpRequestException)
        {
            // Re-throw HTTP exceptions as-is (already logged above)
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Unexpected error while saving flow execution {ExecutionId}", flowExecution.Id);
            throw;
        }
    }
}
