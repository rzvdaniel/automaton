using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/runner-enrollments")]
public sealed class RunnerEnrollmentsController(
    RunnerEnrollmentService runnerEnrollmentService,
    SelfHostedRunnerInstallationService selfHostedRunnerInstallationService) : ControllerBase
{
    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<ActionResult<RunnerEnrollmentExchangeResponse>> Exchange(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await runnerEnrollmentService.ExchangeAsync(request, cancellationToken);
            response ??= await selfHostedRunnerInstallationService.ExchangeAsync(request, cancellationToken);
            return response == null
                ? Unauthorized(new { message = "The enrollment token is invalid, expired, or already used." })
                : Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
