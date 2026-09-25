using Automaton.Api.Application.Commands;
using Automaton.Api.Constants;
using Automaton.Api.Middleware;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Automaton.Api.Controllers;

[Route("api/Account/[action]")]
public sealed class AccountPasswordRecoveryController(ICloudflareTurnstileService cloudflareTurnstileService) : BaseController
{
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthForgotPassword)]
    [EmailRateLimit("auth:forgot-password:email", 3, 3600)]
    public async Task<IActionResult> ForgotUserPassword(
        ForgotUserPasswordCommand forgotUserPassword,
        CancellationToken cancellationToken)
    {
        var isValid = await cloudflareTurnstileService.ValidateAsync(
            forgotUserPassword.TurnstileToken,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (!isValid)
            return BadRequest(new { message = "Verification failed. Please try again." });

        await Mediator.Send(forgotUserPassword, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthResetPassword)]
    public async Task<IActionResult> ResetUserPassword(
        ResetUserPasswordCommand resetUserPassword,
        CancellationToken cancellationToken)
    {
        await Mediator.Send(resetUserPassword, cancellationToken);
        return NoContent();
    }
}
