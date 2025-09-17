using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class FlowExecutionsController : BaseController
{
    private readonly FlowExecutionHistoryService flowExecutionService;
    private readonly FlowLogsService logsService;
    private readonly FlowExecutionLogService flowExecutionLogService;

    public FlowExecutionsController(
        FlowExecutionHistoryService flowExecutionService, 
        FlowLogsService logsService,
        FlowExecutionLogService flowExecutionLogService)
    {
        this.flowExecutionService = flowExecutionService;
        this.logsService = logsService;
        this.flowExecutionLogService = flowExecutionLogService;
    }

    [HttpGet("flow/{flowId}/{startIndex}/{pageSize}")]
    public async Task<ActionResult<IEnumerable<FlowExecution>>> GetForFlow(Guid flowId, int startIndex, int pageSize, CancellationToken cancellationToken)
    {
        var result = await flowExecutionService.GetAsync(flowId, startIndex, pageSize, cancellationToken);

        return Ok(result);
    }

    [HttpGet("recent/{count}")]
    public async Task<ActionResult<IEnumerable<FlowExecution>>> GetRecentExecutions(int count, CancellationToken cancellationToken = default)
    {
        var recentExecutions = await flowExecutionService.GetRecentExecutionsAsync(count, cancellationToken);
        return Ok(recentExecutions);
    }

    [HttpGet("logs/{flowExecutionId}")]
    public async Task<ActionResult<IEnumerable<FlowExecutionLogEntry>>> GetLogs(Guid flowExecutionId, CancellationToken cancellationToken = default)
    {
        var logs = await flowExecutionLogService.GetExecutionLogsAsync(flowExecutionId, cancellationToken);
        return Ok(logs);
    }

    [HttpGet("logs/{flowExecutionId}/text")]
    public async Task<ActionResult<string>> GetLogsText(Guid flowExecutionId, CancellationToken cancellationToken = default)
    {
        var logsText = await flowExecutionLogService.GetExecutionLogsTextAsync(flowExecutionId, cancellationToken);
        return Ok(logsText);
    }

    [HttpGet("logs/{flowExecutionId}/json")]
    public async Task<ActionResult<string>> GetLogsJson(Guid flowExecutionId, CancellationToken cancellationToken = default)
    {
        var logsJson = await flowExecutionLogService.GetExecutionLogsJsonAsync(flowExecutionId, cancellationToken);
        return Ok(logsJson);
    }

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public ActionResult Post(FlowExecution flowExecution, CancellationToken cancellationToken)
    {
        return Ok(flowExecutionService.Add(flowExecution));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, FlowExecution flowExecution, CancellationToken cancellationToken)
    {
        await flowExecutionService.Update(id, flowExecution, cancellationToken);

        return NoContent();
    }
}
