using Automaton.Api.Models;
using Automaton.Api.Authentication;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/infrastructure-runners")]
public sealed class InfrastructureRunnerRegistrationController(
    InfrastructureRunnerRegistrationService registrationService,
    InfrastructureRunnerRabbitMqConfigurationService rabbitMqConfigurationService) : ControllerBase
{
    [HttpPost("register")]
    [Authorize(
        AuthenticationSchemes = ApiKeyAuthenticationSchemeOptions.InfrastructureRunnerEnrollmentScheme,
        Policy = "InfrastructureRunnerEnrollment")]
    public async Task<ActionResult<InfrastructureRunnerRegistrationResponse>> Register(
        InfrastructureRunnerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var authorization = Request.Headers.Authorization.FirstOrDefault();
            if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                apiKey = authorization["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(apiKey))
            return Unauthorized(new { message = "An Infrastructure Runner enrollment API key is required." });

        try
        {
            var response = await registrationService.RegisterAsync(apiKey, request, cancellationToken);
            return response == null
                ? Unauthorized(new { message = "The enrollment API key is invalid, expired, consumed, or has the wrong scope." })
                : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("configuration.env")]
    [AllowAnonymous]
    [Produces("text/plain")]
    public async Task<IActionResult> GetConfiguration(CancellationToken cancellationToken)
    {
        var apiKey = Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
            return Unauthorized();

        var environment = await rabbitMqConfigurationService.ResolveEnvironmentAsync(apiKey, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return environment == null
            ? Unauthorized()
            : Content(environment, "text/plain");
    }
}
