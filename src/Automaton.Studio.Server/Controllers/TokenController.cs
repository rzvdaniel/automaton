using Automaton.Studio.Server.Constants;
using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Common.EF;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Authentication;
using Automaton.Studio.Server.Security;

namespace Automaton.Studio.Server.Controllers;

[Route("api/[controller]/[action]")]
public class TokenController : BaseController
{
    private readonly ConfigurationService configurationService;
    private readonly RefreshTokenCookieService refreshTokenCookieService;

    public TokenController(ConfigurationService configurationService, RefreshTokenCookieService refreshTokenCookieService)
    {
        this.configurationService = configurationService;
        this.refreshTokenCookieService = refreshTokenCookieService;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthRefreshToken)]
    public async Task<IActionResult> RefreshAccessToken(RefreshAccessTokenCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var refreshToken = command.Token ?? refreshTokenCookieService.GetRefreshToken(HttpContext);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                refreshTokenCookieService.ClearRefreshToken(HttpContext);
                return Unauthorized();
            }

            var refreshAccessTokenQuery = new RefreshAccessTokenQuery(configurationService.RefreshTokenLifetime, refreshToken);
            var jwt = await Mediator.Send(refreshAccessTokenQuery, cancellationToken);
            refreshTokenCookieService.AppendRefreshToken(HttpContext, jwt.RefreshToken, configurationService.RefreshTokenLifetime);
            jwt.RefreshToken = string.Empty;

            return Ok(jwt);
        }
        catch (AuthenticationException)
        {
            refreshTokenCookieService.ClearRefreshToken(HttpContext);
            return Unauthorized();
        }
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthRefreshToken)]
    public async Task<IActionResult> RefreshRunnerAccessToken(RefreshAccessTokenCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var refreshAccessTokenQuery = new RefreshAccessTokenQuery(configurationService.RunnerRefreshTokenLifetime, command.Token);
            var jwt = await Mediator.Send(refreshAccessTokenQuery, cancellationToken);
            refreshTokenCookieService.AppendRefreshToken(HttpContext, jwt.RefreshToken, configurationService.RunnerRefreshTokenLifetime);
            jwt.RefreshToken = string.Empty;

            return Ok(jwt);
        }
        catch (AuthenticationException)
        {
            refreshTokenCookieService.ClearRefreshToken(HttpContext);
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
    public async Task<IActionResult> RevokeRefreshToken(RevokeRefreshTokenCommand command, CancellationToken ct)
    {
        var refreshToken = command.Token ?? refreshTokenCookieService.GetRefreshToken(HttpContext);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            refreshTokenCookieService.ClearRefreshToken(HttpContext);
            return NoContent();
        }

        command.UserId = GetUserId();
        command.Token = refreshToken;
        await Mediator.Send(command, ct);
        refreshTokenCookieService.ClearRefreshToken(HttpContext);

        return NoContent();
    }

    [HttpPost]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = refreshTokenCookieService.GetRefreshToken(HttpContext);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await Mediator.Send(new RevokeRefreshTokenCommand(GetUserId(), refreshToken), ct);
        }

        refreshTokenCookieService.ClearRefreshToken(HttpContext);
        return NoContent();
    }
}
