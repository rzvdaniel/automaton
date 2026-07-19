using Automaton.Api.Constants;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/cli-auth")]
public sealed class CliAuthorizationController : BaseController
{
    private readonly CliAuthorizationService authorizationService;
    private readonly ConfigurationService configurationService;
    private readonly UserContextService userContextService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;

    public CliAuthorizationController(
        CliAuthorizationService authorizationService,
        ConfigurationService configurationService,
        UserContextService userContextService,
        ConfirmedEmailGuardService confirmedEmailGuardService)
    {
        this.authorizationService = authorizationService;
        this.configurationService = configurationService;
        this.userContextService = userContextService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
    }

    [HttpPost("requests")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    public async Task<IActionResult> CreateRequest(
        [FromBody] CreateCliAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var studioUrl = string.IsNullOrWhiteSpace(configurationService.WebsiteUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : configurationService.WebsiteUrl.TrimEnd('/');
        var result = await authorizationService.CreateAsync(
            request.DeviceName,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        var verificationUrl = $"{studioUrl}/cli/authorize?requestId={result.RequestId}&code={Uri.EscapeDataString(result.UserCode)}";
        return Ok(new
        {
            requestId = result.RequestId,
            userCode = result.UserCode,
            verifier = result.Verifier,
            verificationUrl,
            expiresIn = (int)Math.Max(1, (result.ExpiresAt - DateTime.UtcNow).TotalSeconds),
            pollInterval = result.PollInterval
        });
    }

    [HttpPost("requests/{requestId:guid}/approve")]
    [Authorize]
    public async Task<IActionResult> Approve(
        Guid requestId,
        [FromBody] ApproveCliAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        var approved = await authorizationService.ApproveAsync(
            requestId,
            request.UserCode,
            userContextService.GetUserId(),
            cancellationToken);

        return approved
            ? Ok(new { approved = true })
            : BadRequest(new { message = "This CLI authorization request is invalid, expired, or already completed." });
    }

    [HttpGet("requests/{requestId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetDetails(
        Guid requestId,
        [FromQuery] string code,
        CancellationToken cancellationToken)
    {
        var details = await authorizationService.GetDetailsAsync(requestId, code, cancellationToken);
        return details is null ? NotFound(new { message = "This CLI authorization request is unavailable." }) : Ok(details);
    }

    [HttpPost("requests/{requestId:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(
        Guid requestId,
        [FromBody] CancelCliAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var cancelled = await authorizationService.CancelAsync(requestId, request.UserCode, cancellationToken);
        return cancelled
            ? Ok(new { cancelled = true })
            : BadRequest(new { message = "This CLI authorization request is invalid, expired, or already completed." });
    }

    [HttpPost("requests/{requestId:guid}/poll")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    public async Task<IActionResult> Poll(
        Guid requestId,
        [FromBody] PollCliAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authorizationService.PollAsync(requestId, request.Verifier, cancellationToken);
        if (result.IsInvalid)
        {
            return Unauthorized(new { message = "Invalid CLI authorization request." });
        }

        return Ok(new
        {
            status = result.Status,
            apiKey = result.ApiKey,
            userId = result.UserId
        });
    }

    [HttpPost("requests/{requestId:guid}/complete")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    public async Task<IActionResult> Complete(
        Guid requestId,
        [FromBody] CompleteCliAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var completed = await authorizationService.CompleteAsync(requestId, request.Verifier, cancellationToken);
        return completed
            ? Ok(new { completed = true })
            : Unauthorized(new { message = "Invalid CLI authorization request." });
    }
}

public sealed class CreateCliAuthorizationRequest
{
    public string? DeviceName { get; set; }
}

public sealed class ApproveCliAuthorizationRequest
{
    public required string UserCode { get; set; }
}

public sealed class PollCliAuthorizationRequest
{
    public required string Verifier { get; set; }
}

public sealed class CompleteCliAuthorizationRequest
{
    public required string Verifier { get; set; }
}

public sealed class CancelCliAuthorizationRequest
{
    public required string UserCode { get; set; }
}
