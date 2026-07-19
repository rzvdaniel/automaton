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

    public CliAuthorizationController(
        CliAuthorizationService authorizationService,
        ConfigurationService configurationService,
        UserContextService userContextService)
    {
        this.authorizationService = authorizationService;
        this.configurationService = configurationService;
        this.userContextService = userContextService;
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
        var approved = await authorizationService.ApproveAsync(
            requestId,
            request.UserCode,
            userContextService.GetUserId(),
            cancellationToken);

        return approved
            ? Ok(new { approved = true })
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
