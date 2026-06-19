using Automaton.Api.Config;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Identity.Abstractions;
using Automaton.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Automaton.Identity.Services;

public sealed class UserRegistrationNotificationService(
    IUserLookupService userLookupService,
    IEmailConfirmationTokenService emailConfirmationTokenService,
    EmailConfirmationLinkBuilder emailConfirmationLinkBuilder,
    IEmailService emailService,
    IOptions<EmailConfig> emailConfigOptions,
    IOptions<AppConfig> appConfigOptions)
{
    private readonly EmailConfig _emailConfig = emailConfigOptions.Value;
    private readonly AppConfig _appConfig = appConfigOptions.Value;

    public async Task SendRegistrationNotificationsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userLookupService.GetByIdAsync(userId, cancellationToken);
        var token = await emailConfirmationTokenService.GenerateEmailConfirmationTokenAsync(userId, cancellationToken);
        var confirmationUrl = emailConfirmationLinkBuilder.Build(userId, token);
        var fromEmail = ResolveUserFacingFromEmail();

        var userDetails = new EmailData
        {
            Template = "AutomatonConfirmEmail",
            From = new EmailAccount { Email = fromEmail },
            To = [new EmailAccount { Email = user.Email!, Name = user.Name }],
            Variables =
            {
                { "Name", user.Name },
                { "ConfirmationUrl", confirmationUrl },
                { "LoginUrl", $"{_appConfig.WebsiteUrl.TrimEnd('/')}/login" },
                { "SupportEmail", _emailConfig.SupportEmail }
            }
        };

        await emailService.Send(userDetails, cancellationToken);

        var notificationRecipient = _emailConfig.AdminEmail;
        if (string.IsNullOrWhiteSpace(notificationRecipient))
        {
            return;
        }

        var adminNotification = new EmailData
        {
            Template = "AutomatonAdminNewRegistration",
            From = new EmailAccount { Email = fromEmail },
            To = [new EmailAccount { Email = notificationRecipient, Name = "Automaton Admin" }],
            Variables =
            {
                { "Name", user.Name ?? string.Empty },
                { "Email", user.Email ?? string.Empty },
                { "PublicUsername", user.PublicUsername ?? string.Empty },
                { "RegisteredAtUtc", user.RegisteredAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") },
                { "AdminUsersUrl", $"{_appConfig.WebsiteUrl.TrimEnd('/')}/admin/users" }
            }
        };

        await emailService.Send(adminNotification, cancellationToken);
    }

    private string ResolveUserFacingFromEmail()
    {
        return string.IsNullOrWhiteSpace(_emailConfig.FromEmail)
            ? _emailConfig.EmailAccount
            : _emailConfig.FromEmail;
    }
}
