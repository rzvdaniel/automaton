using Automaton.Api.Application.Commands;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public class InstallService(
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    ConfigurationService configurationService,
    ApplicationDbContext dbContext,
    ILogger<InstallService> logger,
    UsersService usersService)
{
    private const string CompletionKey = "Installation.Complete";

    public async Task Install(InstallCommand command, CancellationToken cancellationToken)
    {
        await using (var claim = await InstallationClaim.AcquireAsync(dbContext, cancellationToken))
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                : null;
            if (await InstallationComplete())
                throw new InstallationSetupException("Installation has already been completed.");
            await AddRoles(cancellationToken);
            await AddSystemAdministrator(command);
            dbContext.Settings.Add(new Setting { Key = CompletionKey, Value = "true" });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        // SMTP is optional. Delivery must never undo a committed administrator account.
        if (string.IsNullOrWhiteSpace(configurationService.EmailConfig.EmailHost)) return;
        try { await SendRegistrationEmail(command, cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "Administrator created; the welcome email could not be delivered."); }
    }

    public async Task<bool> InstallationComplete()
        => await dbContext.Settings.AnyAsync(setting => setting.Key == CompletionKey)
            || await dbContext.UserRoles.AnyAsync(membership => dbContext.Roles.Any(role =>
            role.Id == membership.RoleId && role.Name == UserRole.SystemAdministrator));

    private Task SendRegistrationEmail(InstallCommand command, CancellationToken cancellationToken)
    {
        var userDetails = new EmailData
        {
            Template = "AutomatonRegisterUser",
            From = new EmailAccount
            {
                Email = string.IsNullOrWhiteSpace(configurationService.EmailConfig.FromEmail)
                    ? configurationService.EmailConfig.EmailAccount : configurationService.EmailConfig.FromEmail
            },
            To = [new EmailAccount { Email = command.Email!, Name = command.Name! }],
            Variables =
            {
                { "Name", command.Name! },
                { "LoginUrl", $"{configurationService.WebsiteUrl}/login" },
                { "SupportEmail", configurationService.EmailConfig.SupportEmail }
            }
        };
        return emailService.Send(userDetails, cancellationToken);
    }

    private async Task AddSystemAdministrator(InstallCommand command)
    {
        var user = new ApplicationUser
        {
            UserName = command.Email, Email = command.Email!, Name = command.Name!,
            PublicUsername = await usersService.GenerateUniquePublicUsernameAsync(command.Name!),
            // Possession of the server setup token authorizes this initial account.
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, command.Password!);
        EnsureSucceeded(result);
        result = await userManager.AddToRolesAsync(user,
            [UserRole.SystemAdministrator, UserRole.Administrator, UserRole.User]);
        EnsureSucceeded(result);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InstallationSetupException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    private async Task AddRoles(CancellationToken cancellationToken)
    {
        string[] roles = [UserRole.SystemAdministrator, UserRole.Administrator, UserRole.User];
        foreach (var role in roles)
        {
            if (!await dbContext.Roles.AnyAsync(item => item.Name == role, cancellationToken))
                dbContext.Roles.Add(new ApplicationRole { Name = role, NormalizedName = role.ToUpperInvariant() });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
