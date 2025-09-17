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
                log.StepId,
                log.StepName,
                log.Exception
            }).ToArray()
        };

        var response = await httpClient.PostAsJsonAsync(configService.FlowExecutionUrl, flowExecutionModel);
        response.EnsureSuccessStatusCode();
    }
}
