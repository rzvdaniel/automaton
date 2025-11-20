using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class FlowExecutionsController : BaseController
{
    private readonly FlowExecutionHistoryService flowExecutionService;
    private readonly FlowExecutionLogService flowExecutionLogService;

    public FlowExecutionsController(
        FlowExecutionHistoryService flowExecutionService, 
        FlowExecutionLogService flowExecutionLogService)
    {
        this.flowExecutionService = flowExecutionService;
        this.flowExecutionLogService = flowExecutionLogService;
    }

    [HttpGet("flow/{flowId}/{startIndex}/{pageSize}")]
    public async Task<ActionResult<FlowExecutionResult>> GetForFlow(Guid flowId, int startIndex, int pageSize, CancellationToken cancellationToken)
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
    public async Task<ActionResult> Post([FromBody] FlowExecution flowExecution, CancellationToken cancellationToken = default)
    {
        try
        {
            // Basic model validation
            if (flowExecution == null)
            {
                return BadRequest("Flow execution data is required.");
            }

            if (flowExecution.Id == Guid.Empty)
            {
                return BadRequest("Flow execution ID is required.");
            }

            if (flowExecution.FlowId == Guid.Empty)
            {
                return BadRequest("Flow ID is required.");
            }

            // Log the incoming request for debugging
            var logger = HttpContext.RequestServices.GetService<ILogger<FlowExecutionsController>>();
            logger?.LogInformation("Received flow execution save request for {ExecutionId} of flow {FlowId} with {LogCount} logs", 
                flowExecution.Id, flowExecution.FlowId, flowExecution.Logs?.Count ?? 0);

            var result = await Task.Run(() => flowExecutionService.Add(flowExecution), cancellationToken);
            
            logger?.LogInformation("Successfully saved flow execution {ExecutionId}", flowExecution.Id);
            return Ok(new { Success = true, Message = "Flow execution saved successfully", ExecutionId = flowExecution.Id });
        }
        catch (Exception ex)
        {
            var logger = HttpContext.RequestServices.GetService<ILogger<FlowExecutionsController>>();
            logger?.LogError(ex, "Failed to save flow execution {ExecutionId}", flowExecution?.Id);
            
            return StatusCode(500, new
            {
                Success = false,
                Message = "An error occurred while saving the flow execution",
                Error = ex.Message,
                ExecutionId = flowExecution?.Id
            });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(Guid id, FlowExecution flowExecution, CancellationToken cancellationToken)
    {
        await flowExecutionService.Update(id, flowExecution, cancellationToken);

        return NoContent();
    }
}
