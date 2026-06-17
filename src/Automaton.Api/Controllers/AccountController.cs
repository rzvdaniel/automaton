using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Models;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Application.Commands;
using Automaton.Api.Data;
using Automaton.Api.Security;
using Automaton.Api.Authentication;
using Automaton.Api.Middleware;
using Automaton.Api.Application.Queries;

namespace Automaton.Api.Controllers;

[Route("api/[controller]/[action]")]
public class AccountController : BaseController
{
    private readonly ConfigurationService configurationService;
    private readonly ApplicationDbContext dataContext;
    private readonly UsersService userManagerService;
    private readonly IEmailService emailService;
    private readonly ICloudflareTurnstileService cloudflareTurnstileService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly RefreshTokenCookieService refreshTokenCookieService;
    private readonly ILogger<AccountController> logger;

    public AccountController(ConfigurationService configurationService,
        ApplicationDbContext dataContext,
        UsersService userManagerService,
        IEmailService emailService,
        ICloudflareTurnstileService cloudflareTurnstileService,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        RefreshTokenCookieService refreshTokenCookieService,
        ILogger<AccountController> logger)
    {
        this.configurationService = configurationService;
        this.dataContext = dataContext;
        this.userManagerService = userManagerService;
        this.emailService = emailService;
        this.cloudflareTurnstileService = cloudflareTurnstileService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.refreshTokenCookieService = refreshTokenCookieService;
        this.logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthRegistration)]
    [EmailRateLimit("auth:register:email", 3, 3600)]
    public async Task<IActionResult> RegisterUser(RegisterUserCommand registerUserCommand)
    {
        if (configurationService.NoUserSignUp)
        {
            return NotFound();
        }

        if (!await ValidateTurnstileAsync(registerUserCommand.TurnstileToken, CancellationToken.None))
        {
            return BadRequest(new { message = "Verification failed. Please try again." });
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        string publicUsername;

        if (!string.IsNullOrWhiteSpace(registerUserCommand.PublicUsername))
        {
            if (!userManagerService.IsValidPublicUsername(registerUserCommand.PublicUsername))
            {
                ModelState.AddModelError("PublicUsername", "Public username must be 3-32 characters, using only letters, numbers, hyphens, and underscores.");
                return BadRequest(ModelState);
            }

            var isAvailable = await userManagerService.IsPublicUsernameAvailableAsync(registerUserCommand.PublicUsername);
            if (!isAvailable)
            {
                ModelState.AddModelError("PublicUsername", "This public username is already taken. Please choose another.");
                return BadRequest(ModelState);
            }

            publicUsername = registerUserCommand.PublicUsername;
        }
        else
        {
            publicUsername = await userManagerService.GenerateUniquePublicUsernameAsync(registerUserCommand.Name!);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Name = registerUserCommand.Name,
            Email = registerUserCommand.Email,
            UserName = registerUserCommand.Email,
            PublicUsername = publicUsername,
            SecurityStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            RegisteredAtUtc = DateTime.UtcNow,
            RegistrationProvider = RegistrationProviders.Password
        };

        await userManagerService.CreateUser(user, registerUserCommand.Password);
        await dataContext.SaveChangesAsync();

        try
        {
            await SendEmailConfirmationAsync(user, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Confirmation email could not be sent to {Email}", registerUserCommand.Email);
        }

        try
        {
            await SendAdminRegistrationNotificationAsync(user, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin registration notification could not be sent for user {UserId}", user.Id);
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            name = registerUserCommand.Name,
            publicUsername = publicUsername
        });
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

    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicyNames.AuthResendConfirmation)]
    public async Task<IActionResult> ResendConfirmation(CancellationToken cancellationToken)
    {
        var user = await userManagerService.GetUserById(GetUserId());

        if (user.EmailConfirmed)
        {
            return NoContent();
        }

        try
        {
            await SendEmailConfirmationAsync(user, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Confirmation email resend failed for user {UserId}", user.Id);
        }

        return NoContent();
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(Guid userId, string token, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return Redirect(BuildEmailConfirmationRedirectUrl(success: false));
        }

        try
        {
            var result = await userManagerService.ConfirmEmailAsync(userId, token);
            if (!result.Succeeded)
            {
                return Redirect(BuildEmailConfirmationRedirectUrl(success: false));
            }

            await dataContext.SaveChangesAsync(cancellationToken);
            return Redirect(BuildEmailConfirmationRedirectUrl(success: true));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Email confirmation failed for user {UserId}", userId);
            return Redirect(BuildEmailConfirmationRedirectUrl(success: false));
        }
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthForgotPassword)]
    [EmailRateLimit("auth:forgot-password:email", 3, 3600)]
    public async Task<IActionResult> ForgotUserPassword(ForgotUserPasswordCommand forgotUserPassword, CancellationToken cancellationToken)
    {
        if (!await ValidateTurnstileAsync(forgotUserPassword.TurnstileToken, cancellationToken))
        {
            return BadRequest(new { message = "Verification failed. Please try again." });
        }

        await Mediator.Send(forgotUserPassword, cancellationToken);

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

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthResetPassword)]
    public async Task<IActionResult> ResetUserPassword(ResetUserPasswordCommand resetUserPassword, CancellationToken cancellationToken)
    {
        await Mediator.Send(resetUserPassword, cancellationToken);

        return NoContent();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetSubscriptionPlans([FromServices] ApplicationDbContext db)
    {
        var canAccessHostedRunnerTestPlans = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == GetUserId())
            .Select(x => x.CanAccessHostedRunnerTestPlans)
            .FirstOrDefaultAsync();

        var plans = await db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.PriceUsd,
                Capabilities = PlanCapabilitiesSerializer.Deserialize(p.CapabilitiesJson),
                p.IsDefault,
                p.IsActive,
                p.SortOrder
            })
            .ToListAsync();

        return Ok(plans.Where(plan =>
            HostedRunnerPlanVisibility.IsVisibleInCatalog(plan.Capabilities, canAccessHostedRunnerTestPlans)));
    }

    private async Task<bool> ValidateTurnstileAsync(string? token, CancellationToken cancellationToken)
    {
        return await cloudflareTurnstileService.ValidateAsync(
            token,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
    }

    private async Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await userManagerService.GenerateEmailConfirmationTokenAsync(user.Id);
        var confirmationUrl = BuildEmailConfirmationUrl(user.Id, token);

        var userDetails = new EmailData
        {
            Template = "AutomatonConfirmEmail",
            From = new EmailAccount { Email = ResolveUserFacingFromEmail() },
            To = [new EmailAccount { Email = user.Email, Name = user.Name }],
            Variables =
            {
                { "Name", user.Name },
                { "ConfirmationUrl", confirmationUrl },
                { "LoginUrl", $"{configurationService.WebsiteUrl}/login" },
                { "SupportEmail", configurationService.EmailConfig.SupportEmail }
            }
        };

        await emailService.Send(userDetails, cancellationToken);
    }

    private string BuildEmailConfirmationUrl(Guid userId, string token)
    {
        var apiBaseUrl = $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
        var query = new Dictionary<string, string?>
        {
            ["userId"] = userId.ToString(),
            ["token"] = token
        };

        return QueryHelpers.AddQueryString(
            $"{apiBaseUrl}/api/Account/ConfirmEmail",
            query);
    }

    private string BuildEmailConfirmationRedirectUrl(bool success)
    {
        var baseUrl = configurationService.WebsiteUrl.TrimEnd('/');
        var targetPath = success ? "/" : "/login";

        return QueryHelpers.AddQueryString(
            $"{baseUrl}{targetPath}",
            new Dictionary<string, string?>
            {
                ["emailConfirmation"] = success ? "success" : "failed"
            });
    }

    private async Task SendAdminRegistrationNotificationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var notificationRecipient = configurationService.EmailConfig.AdminEmail;
        if (string.IsNullOrWhiteSpace(notificationRecipient))
        {
            return;
        }

        var adminNotification = new EmailData
        {
            Template = "AutomatonAdminNewRegistration",
            From = new EmailAccount { Email = ResolveUserFacingFromEmail() },
            To = [new EmailAccount { Email = notificationRecipient, Name = "Automaton Admin" }],
            Variables =
            {
                { "Name", user.Name ?? string.Empty },
                { "Email", user.Email ?? string.Empty },
                { "PublicUsername", user.PublicUsername ?? string.Empty },
                { "RegisteredAtUtc", user.RegisteredAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") },
                { "AdminUsersUrl", $"{configurationService.WebsiteUrl.TrimEnd('/')}/admin/users" }
            }
        };

        await emailService.Send(adminNotification, cancellationToken);
    }

    private string ResolveUserFacingFromEmail()
    {
        return string.IsNullOrWhiteSpace(configurationService.EmailConfig.FromEmail)
            ? configurationService.EmailConfig.EmailAccount
            : configurationService.EmailConfig.FromEmail;
    }
}
