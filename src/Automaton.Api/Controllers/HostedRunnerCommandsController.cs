using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/hosted-runner-commands")]
public sealed class HostedRunnerCommandsController(
    HostedRunnerBrokerConfigurationService brokerConfiguration) : ControllerBase
{
    [HttpGet("configuration")]
    public async Task<ActionResult<HostedRunnerBrokerConfiguration>> Configuration(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        CancellationToken cancellationToken)
    {
        var result = await brokerConfiguration.ProvisionAsync(apiKey, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return result is null ? Unauthorized() : Ok(result);
    }
}
