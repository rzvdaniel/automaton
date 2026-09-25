using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Automaton.Api.Authentication;
using Automaton.Api.Constants;
using Automaton.Api.Services;
using Automaton.Api.Models;
using Automaton.Api.Application.Commands;
using Automaton.Api.Data;
using Automaton.Api.Application.Queries;
using Automaton.Api.Middleware;
using Automaton.Api.Security;

namespace Automaton.Api.Controllers;

[Route("api/[controller]/[action]")]
public class AccountController : BaseController
{
    private readonly ConfigurationService configurationService;
    private readonly ApplicationDbContext dataContext;
    private readonly UsersService userManagerService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly RefreshTokenCookieService refreshTokenCookieService;

    public AccountController(ConfigurationService configurationService,
        ApplicationDbContext dataContext,
        UsersService userManagerService,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        RefreshTokenCookieService refreshTokenCookieService)
    {
        this.configurationService = configurationService;
        this.dataContext = dataContext;
        this.userManagerService = userManagerService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.refreshTokenCookieService = refreshTokenCookieService;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthLogin)]
    [EmailRateLimit("auth:login:email", 10, 900)]
    public async Task<ActionResult<JsonWebToken>> LoginUser(SignInUserCommand signInUserCommand, CancellationToken cancellationToken)
    {
        var signInUserQuery = new SignInUserQuery(configurationService.RefreshTokenLifetime, signInUserCommand.Email, signInUserCommand.Password);
        var jwt = await Mediator.Send(signInUserQuery, cancellationToken);
        refreshTokenCookieService.AppendRefreshToken(HttpContext, jwt.RefreshToken, configurationService.RefreshTokenLifetime);
        jwt.RefreshToken = string.Empty;

        return Ok(jwt);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateUserPassword(UpdateUserPasswordCommand passwordUpdateCommand, CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        var userId = GetUserId();

        await userManagerService.UpdatePassword
        (
            userId,
            passwordUpdateCommand.OldPassword,
            passwordUpdateCommand.NewPassword
        );

        await userManagerService.RevokeAllRefreshTokensAsync(userId, cancellationToken);
        refreshTokenCookieService.ClearRefreshToken(HttpContext);

        await dataContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPut]
    public async Task<IActionResult> UpdateUserProfile(UpdateUserInfoCommand profileUpdateCommand, CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        profileUpdateCommand.Id = GetUserId();

        await Mediator.Send(profileUpdateCommand, cancellationToken);

        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<string>> GetPublicUsername()
    {
        var userId = GetUserId();
        var user = await userManagerService.GetUserById(userId);

        return Ok(user.PublicUsername);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<bool>> IsPublicUsernameAvailable(string publicUsername)
    {
        var isAvailable = await userManagerService.IsPublicUsernameAvailableAsync(publicUsername);
        return Ok(isAvailable);
    }

    [HttpPut]
    public async Task<IActionResult> UpdatePublicUsername([FromBody] UpdatePublicUsernameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.NewPublicUsername))
        {
            ModelState.AddModelError("NewPublicUsername", "Public username is required");
            return BadRequest(ModelState);
        }

        if (!userManagerService.IsValidPublicUsername(request.NewPublicUsername))
        {
            ModelState.AddModelError("NewPublicUsername", "Public username must be 3-32 characters, using only letters, numbers, hyphens, and underscores.");
            return BadRequest(ModelState);
        }

        var userId = GetUserId();
        var currentUser = await userManagerService.GetUserById(userId);

        if (!string.Equals(currentUser?.PublicUsername, request.NewPublicUsername, StringComparison.OrdinalIgnoreCase))
        {
            var isAvailable = await userManagerService.IsPublicUsernameAvailableAsync(request.NewPublicUsername);
            if (!isAvailable)
            {
                ModelState.AddModelError("NewPublicUsername", "This public username is already taken. Please choose another.");
                return BadRequest(ModelState);
            }
        }

        await userManagerService.UpdatePublicUsernameAsync(userId, request.NewPublicUsername);
        await dataContext.SaveChangesAsync();

        return Ok(new { publicUsername = request.NewPublicUsername });
    }
}
