using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Authentication;
using Automaton.Api.Services;
using Automaton.Api.Constants;
using Automaton.Api.Application.Commands;
using Automaton.Api.Security;
using Automaton.Api.Application.Queries;

namespace Automaton.Api.Controllers;

[Route("api/[controller]/[action]")]
public class TokenController : BaseController
{
    private readonly ConfigurationService configurationService;
    public TokenController(ConfigurationService configurationService)
    {
        this.configurationService = configurationService;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthRefreshToken)]
    public async Task<IActionResult> RefreshAccessToken(RefreshAccessTokenCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var refreshToken = command.Token ?? RefreshTokenCookieService.GetRefreshToken(HttpContext);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                RefreshTokenCookieService.ClearRefreshToken(HttpContext);
                return Unauthorized();
            }

            var refreshAccessTokenQuery = new RefreshAccessTokenQuery(configurationService.RefreshTokenLifetime, refreshToken);
            var jwt = await Mediator.Send(refreshAccessTokenQuery, cancellationToken);
            RefreshTokenCookieService.AppendRefreshToken(HttpContext, jwt.RefreshToken, configurationService.RefreshTokenLifetime);
            jwt.RefreshToken = string.Empty;

            return Ok(jwt);
        }
        catch (AuthenticationException)
        {
            RefreshTokenCookieService.ClearRefreshToken(HttpContext);
            return Unauthorized();
        }
    }

    [HttpPost]
    public async Task<IActionResult> RevokeAccessToken(RevokeAccessTokenCommand command, CancellationToken ct)
    {
        await Mediator.Send(command, ct);

        return NoContent();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> RevokeRefreshToken(RevokeRefreshTokenCommand command, CancellationToken ct)
    {
        var refreshToken = command.Token ?? RefreshTokenCookieService.GetRefreshToken(HttpContext);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            RefreshTokenCookieService.ClearRefreshToken(HttpContext);
            return NoContent();
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            command.UserId = GetUserId();
        }

        command.Token = refreshToken;
        await Mediator.Send(command, ct);
        RefreshTokenCookieService.ClearRefreshToken(HttpContext);

        return NoContent();
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = RefreshTokenCookieService.GetRefreshToken(HttpContext);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var userId = User.Identity?.IsAuthenticated == true ? GetUserId() : Guid.Empty;
            await Mediator.Send(new RevokeRefreshTokenCommand(userId, refreshToken), ct);
        }

        RefreshTokenCookieService.ClearRefreshToken(HttpContext);
        return NoContent();
    }
}
