using Automaton.Api.Authentication;
using Automaton.Api.Application.Commands;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Middleware;
using Automaton.Api.Models;
using Automaton.Api.Security;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Automaton.Api.Controllers;

[Route("api/Account/[action]")]
public sealed class AccountRegistrationController : BaseController
{
    private readonly ConfigurationService configurationService;
    private readonly ApplicationDbContext dataContext;
    private readonly UsersService userManagerService;
    private readonly IEmailService emailService;
    private readonly EmailConfirmationLinkBuilder emailConfirmationLinkBuilder;
    private readonly ICloudflareTurnstileService cloudflareTurnstileService;
    private readonly AccountRegistrationService accountRegistrationService;
    private readonly ILogger<AccountRegistrationController> logger;

    public AccountRegistrationController(
        ConfigurationService configurationService,
        ApplicationDbContext dataContext,
        UsersService userManagerService,
        IEmailService emailService,
        EmailConfirmationLinkBuilder emailConfirmationLinkBuilder,
        ICloudflareTurnstileService cloudflareTurnstileService,
        AccountRegistrationService accountRegistrationService,
        ILogger<AccountRegistrationController> logger)
    {
        this.configurationService = configurationService;
        this.dataContext = dataContext;
        this.userManagerService = userManagerService;
        this.emailService = emailService;
        this.emailConfirmationLinkBuilder = emailConfirmationLinkBuilder;
        this.cloudflareTurnstileService = cloudflareTurnstileService;
        this.accountRegistrationService = accountRegistrationService;
        this.logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult RegistrationStatus() => Ok(new
    {
        enabled = !configurationService.NoUserSignUp,
        message = configurationService.NoUserSignUp ? ConfigurationService.RegistrationPausedMessage : null
    });

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.AuthRegistration)]
    [EmailRateLimit("auth:register:email", 3, 3600)]
    public async Task<IActionResult> RegisterUser(RegisterUserCommand registerUserCommand)
    {
        if (configurationService.NoUserSignUp)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfigurationService.RegistrationPausedMessage });

        if (!await ValidateTurnstileAsync(registerUserCommand.TurnstileToken, CancellationToken.None))
            return BadRequest(new { message = "Verification failed. Please try again." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var publicUsername = await ResolvePublicUsernameAsync(registerUserCommand);
        if (publicUsername is null)
            return BadRequest(ModelState);

        var user = CreateUser(registerUserCommand, publicUsername);
        await accountRegistrationService.RegisterAsync(user, registerUserCommand.Password, GetCorrelationId());

        return StatusCode(StatusCodes.Status201Created, new
        {
            name = registerUserCommand.Name,
            publicUsername
        });
    }

    [HttpPost]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicyNames.AuthResendConfirmation)]
    public async Task<IActionResult> ResendConfirmation(CancellationToken cancellationToken)
    {
        var user = await userManagerService.GetUserById(GetUserId());
        if (user.EmailConfirmed)
            return NoContent();

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
            return Redirect(BuildEmailConfirmationRedirectUrl(success: false));

        try
        {
            var result = await userManagerService.ConfirmEmailAsync(userId, token);
            if (!result.Succeeded)
                return Redirect(BuildEmailConfirmationRedirectUrl(success: false));

            await dataContext.SaveChangesAsync(cancellationToken);
            return Redirect(BuildEmailConfirmationRedirectUrl(success: true));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Email confirmation failed for user {UserId}", userId);
            return Redirect(BuildEmailConfirmationRedirectUrl(success: false));
        }
    }

    private async Task<string?> ResolvePublicUsernameAsync(RegisterUserCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.PublicUsername))
            return await userManagerService.GenerateUniquePublicUsernameAsync(command.Name!);

        if (!UsersService.IsValidPublicUsername(command.PublicUsername))
        {
            ModelState.AddModelError("PublicUsername", "Public username must be 3-32 characters, using only letters, numbers, hyphens, and underscores.");
            return null;
        }

        if (!await userManagerService.IsPublicUsernameAvailableAsync(command.PublicUsername))
        {
            ModelState.AddModelError("PublicUsername", "This public username is already taken. Please choose another.");
            return null;
        }

        return command.PublicUsername;
    }

    private static ApplicationUser CreateUser(RegisterUserCommand command, string publicUsername) => new()
    {
        Id = Guid.NewGuid(),
        Name = command.Name,
        Email = command.Email,
        UserName = command.Email,
        PublicUsername = publicUsername,
        SecurityStamp = Guid.NewGuid().ToString(),
        LockoutEnabled = true,
        RegisteredAtUtc = DateTime.UtcNow,
        RegistrationProvider = RegistrationProviders.Password
    };

    private Task<bool> ValidateTurnstileAsync(string? token, CancellationToken cancellationToken) =>
        cloudflareTurnstileService.ValidateAsync(
            token,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

    private async Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = await userManagerService.GenerateEmailConfirmationTokenAsync(user.Id);
        var confirmationUrl = emailConfirmationLinkBuilder.Build(user.Id, token);
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

    private string ResolveUserFacingFromEmail() =>
        string.IsNullOrWhiteSpace(configurationService.EmailConfig.FromEmail)
            ? configurationService.EmailConfig.EmailAccount
            : configurationService.EmailConfig.FromEmail;
}
