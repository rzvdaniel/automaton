using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class FlowExecutionsController : BaseController
{
    private readonly FlowExecutionHistoryService flowExecutionService;
    private readonly FlowLogsService logsService;

    public FlowExecutionsController(FlowExecutionHistoryService flowExecutionService, FlowLogsService logsService)
    {
        this.flowExecutionService = flowExecutionService;
        this.logsService = logsService;
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
    public ActionResult<IEnumerable<Entities.Log>> GetLogs(Guid flowExecutionId)
    {
        return Ok(logsService.GetFlowExecutionLogs(flowExecutionId));
    }

    [HttpPost]
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
