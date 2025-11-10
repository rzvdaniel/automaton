using Automaton.Studio.Models;
using Automaton.Studio.Services;

namespace Automaton.Studio.Pages.Flows.Designer;

public class FlowActivityViewModel
{
    private readonly FlowExecutionsService flowExecutionService;

    public IEnumerable<FlowExecution> Executions { get; set; } = new List<FlowExecution>();
    public int Total { get; set; }

    public FlowActivityViewModel
    (
        FlowExecutionsService flowExecutionService,
        FlowService flowService
    )
    {
        this.flowExecutionService = flowExecutionService;
    }

    public async Task GetFlowActivity(string flowIdString, int startIndex, int pageSize)
    {
        Guid.TryParse(flowIdString, out var flowId);
        var result = await flowExecutionService.GetFlowExecutionResult(flowId, startIndex, pageSize);

        Executions = result.FlowExecutions;
        Total = result.Total;
    }

    public async Task<IEnumerable<FlowExecutionLogEntry>> GetLogs(string flowIdString, Guid flowExecutionId)
    {
        Guid.TryParse(flowIdString, out var flowId);
        var logs = await flowExecutionService.GetLogsActivity(flowId, flowExecutionId);

        return logs;
    }

    public async Task<string> GetLogsText(string flowIdString, Guid flowExecutionId)
    {
        Guid.TryParse(flowIdString, out var flowId);
        var logsText = await flowExecutionService.GetLogsActivityText(flowId, flowExecutionId);

        return logsText;
    }

    public async Task<string> GetLogsJson(string flowIdString, Guid flowExecutionId)
    {
        Guid.TryParse(flowIdString, out var flowId);
        var logsJson = await flowExecutionService.GetLogsActivityJson(flowId, flowExecutionId);

        return logsJson;
    }
}
