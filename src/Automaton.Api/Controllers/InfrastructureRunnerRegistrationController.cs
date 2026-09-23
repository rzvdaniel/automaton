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
        [FromHeader(Name = "X-API-Key")] string[]? apiKeyHeaders,
        [FromHeader(Name = "Authorization")] string[]? authorizationHeaders,
        CancellationToken cancellationToken)
    {
        var apiKey = apiKeyHeaders?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var authorizationHeader = authorizationHeaders?.FirstOrDefault();
            if (authorizationHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                apiKey = authorizationHeader["Bearer ".Length..].Trim();
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
    public async Task<IActionResult> GetConfiguration(
        [FromHeader(Name = "X-API-Key")] string[]? apiKeyHeaders,
        [FromHeader(Name = "X-Runner-Enrollment-Token")] string[]? enrollmentTokenHeaders,
        CancellationToken cancellationToken)
    {
        var apiKey = apiKeyHeaders?.FirstOrDefault();
        var enrollmentToken = enrollmentTokenHeaders?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(enrollmentToken))
            return Unauthorized();

        var environment = await rabbitMqConfigurationService.ResolveEnvironmentAsync(
            apiKey,
            enrollmentToken,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return environment == null
            ? Unauthorized()
            : Content(environment, "text/plain");
    }
}
