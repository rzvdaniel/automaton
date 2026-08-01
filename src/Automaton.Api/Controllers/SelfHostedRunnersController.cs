using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/self-hosted-runners")]
[Authorize(Policy = SelfHostedRunnerInstallationAuthorization.PolicyName)]
public sealed class SelfHostedRunnersController(
    SelfHostedRunnerInstallationService installationService) : ControllerBase
{
    [HttpPost("installations")]
    public async Task<ActionResult<SelfHostedRunnerInstallationResponse>> CreateInstallation(
        CreateSelfHostedRunnerInstallationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await installationService.CreateAsync(request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
