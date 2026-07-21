using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/infrastructure-actions")]
[Authorize(Policy = "Administrators")]
public sealed class AdminInfrastructureActionsController : ControllerBase
{
    private readonly SystemInfrastructureAppService infrastructure;
    private readonly UserContextService userContext;

    public AdminInfrastructureActionsController(SystemInfrastructureAppService infrastructure, UserContextService userContext)
    {
        this.infrastructure = infrastructure;
        this.userContext = userContext;
    }

    [HttpPost("runner-info")]
    public async Task<IActionResult> RunnerInfo([FromBody] InfrastructureRunnerInfoRequest request, CancellationToken cancellationToken)
        => Ok(await infrastructure.ExecuteRunnerInfoAsync(request.RunnerId, userContext.GetUserId(), cancellationToken));
}

public sealed record InfrastructureRunnerInfoRequest(Guid RunnerId);
