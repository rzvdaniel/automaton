using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/infrastructure-workflows")]
[Authorize(Policy = "Administrators")]
public sealed class AdminInfrastructureWorkflowsController(
    InfrastructureWorkflowExecutionService workflowService,
    UserContextService userContextService) : ControllerBase
{
    [HttpPost("host-validation")]
    public async Task<ActionResult<InfrastructureWorkflowExecutionResponse>> StartHostValidation(
        StartHostValidationRequest request,
        CancellationToken cancellationToken)
        => Ok(await workflowService.StartHostValidationAsync(
            request,
            userContextService.GetUserId(),
            cancellationToken));

    [HttpGet("executions/{id:guid}")]
    public async Task<ActionResult<InfrastructureWorkflowExecutionResponse>> GetExecution(
        Guid id,
        CancellationToken cancellationToken)
    {
        var execution = await workflowService.GetAsync(id, cancellationToken);
        return execution == null ? NotFound() : Ok(execution);
    }

    [HttpGet("executions/{id:guid}/logs")]
    public async Task<ActionResult<IReadOnlyList<InfrastructureWorkflowExecutionLogResponse>>> GetLogs(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await workflowService.GetLogsAsync(id, cancellationToken));
}
