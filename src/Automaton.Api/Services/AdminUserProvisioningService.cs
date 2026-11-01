using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Automaton.Api.Services;

public sealed record AdminInviteUserRequest(string Email, string? Name = null, bool Administrator = false);

public sealed record AdminCreateUserRequest(string Email, string? Name = null, string? Password = null, bool Administrator = false);

/// <summary>The outcome of an invite or create. <c>GeneratedPassword</c> is returned once and never stored.</summary>
public sealed record AdminProvisionedUser(
    Guid UserId,
    string Email,
    string Name,
    bool InvitationSent,
    bool Resent,
    string? GeneratedPassword,
    string? Warning);

/// <summary>An account already exists for the email address.</summary>
public sealed class AdminUserConflictException(string message) : InvalidOperationException(message);

/// <summary>
/// How users join a self-managed server, since public sign-up is disabled: a system or regular administrator
/// invites them by email (one-time "set your password" link) or creates the account with a password
/// (for test users and people without email). Granting the administrator role is left to system administrators.
/// </summary>
public sealed class AdminUserProvisioningService(
    UserManager<ApplicationUser> userManager,
    UsersService users,
    IEmailService email,
    ConfigurationService configuration,
    IAuditEventService audit)
{
    public const string InvitationTemplate = "AutomatonUserInvitation";
    private const int GeneratedPasswordLength = 20;

    public async Task<AdminProvisionedUser> InviteAsync(AdminInviteUserRequest request, string? invitedByName,
        bool callerIsSystemAdministrator, CancellationToken token)
    {
        var address = NormalizeEmail(request.Email);
        EnsureMayGrant(request.Administrator, callerIsSystemAdministrator);

        var user = await userManager.FindByEmailAsync(address);
        var resent = user is not null;
        if (user is not null && await userManager.HasPasswordAsync(user))
            throw new AdminUserConflictException($"{address} already has an account. Use Forgot password to recover it.");
        if (user is null)
        {
            user = await NewUserAsync(address, request.Name, RegistrationProviders.Invitation);
            await users.CreateUserWithoutPasswordAsync(user);
            if (request.Administrator) await AddAdministratorAsync(user);
        }

        string? warning = null;
        var sent = true;
        try
        {
            sent = await SendInvitationAsync(user, invitedByName, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The account exists either way; the administrator can send the invitation again.
            sent = false;
        }
        if (!sent)
        {
            warning = "The account was created, but the invitation email could not be sent. Check Admin → Installation → SMTP, then invite the same address again to resend.";
        }

        await audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = resent ? AuditActions.Requested : AuditActions.Created,
            EntityType = AuditEntityTypes.User,
            EntityId = user.Id,
            TargetUserId = user.Id,
            EventType = AuditEventTypes.UserInvited,
            Outcome = sent ? AuditOutcomes.Success : AuditOutcomes.Failed,
            Message = resent ? "User invitation sent again." : "User invited by an administrator.",
            Metadata = new { Email = address, request.Administrator, InvitationSent = sent, Resent = resent }
        }, token);
        return new AdminProvisionedUser(user.Id, address, user.Name, sent, resent, null, warning);
    }

    public async Task<AdminProvisionedUser> CreateAsync(AdminCreateUserRequest request, bool callerIsSystemAdministrator,
        CancellationToken token)
    {
        var address = NormalizeEmail(request.Email);
        EnsureMayGrant(request.Administrator, callerIsSystemAdministrator);
        if (await userManager.FindByEmailAsync(address) is not null)
            throw new AdminUserConflictException($"{address} already has an account.");

        var generated = string.IsNullOrEmpty(request.Password);
        var password = generated ? GeneratePassword() : request.Password!;
        var user = await NewUserAsync(address, request.Name, RegistrationProviders.AdminCreated);
        // The administrator vouches for the address; no confirmation email is sent.
        user.EmailConfirmed = true;
        await users.CreateUser(user, password);
        if (request.Administrator) await AddAdministratorAsync(user);

        await audit.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.User,
            EntityId = user.Id,
            TargetUserId = user.Id,
            EventType = AuditEventTypes.UserCreatedByAdministrator,
            Outcome = AuditOutcomes.Success,
            Message = "User created by an administrator.",
            Metadata = new { Email = address, request.Administrator, PasswordGenerated = generated }
        }, token);
        return new AdminProvisionedUser(user.Id, address, user.Name, false, false, generated ? password : null, null);
    }

    /// <summary>
    /// A one-time link to the Studio page that sets the password. It is an Identity password reset token:
    /// valid for 24 hours and invalid once a password is set (the security stamp changes).
    /// </summary>
    public static async Task<string> CreatePasswordSetupUrlAsync(UserManager<ApplicationUser> userManager,
        ApplicationUser user, string websiteUrl)
    {
        var rawToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
        var address = user.Email ?? throw new InvalidOperationException("A user email is required to create a password setup link.");
        return QueryHelpers.AddQueryString($"{websiteUrl.TrimEnd('/')}/reset-password",
            new Dictionary<string, string?> { ["email"] = address, ["code"] = code, ["create"] = "true" });
    }

    /// <summary>A random password with every character class, so it passes any configured password policy.</summary>
    public static string GeneratePassword(int length = GeneratedPasswordLength)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789", symbols = "!@#$%^&*-_=+";
        const string all = upper + lower + digits + symbols;
        var chars = new List<char>
        {
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };
        while (chars.Count < length) chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    private async Task<ApplicationUser> NewUserAsync(string address, string? name, string provider) => new()
    {
        Id = Guid.NewGuid(),
        Name = string.IsNullOrWhiteSpace(name) ? address.Split('@')[0] : name.Trim(),
        Email = address,
        UserName = address,
        PublicUsername = await users.GenerateUniquePublicUsernameAsync(string.IsNullOrWhiteSpace(name) ? address : name),
        SecurityStamp = Guid.NewGuid().ToString(),
        LockoutEnabled = true,
        RegisteredAtUtc = DateTime.UtcNow,
        RegistrationProvider = provider
    };

    private async Task AddAdministratorAsync(ApplicationUser user)
    {
        var result = await userManager.AddToRoleAsync(user, UserRole.Administrator);
        if (!result.Succeeded) throw new InvalidOperationException("The account was created, but the administrator role could not be added.");
    }

    private async Task<bool> SendInvitationAsync(ApplicationUser user, string? invitedByName, CancellationToken token)
    {
        var website = configuration.WebsiteUrl.TrimEnd('/');
        var setupUrl = await CreatePasswordSetupUrlAsync(userManager, user, website);
        return await email.Send(new EmailData
        {
            Template = InvitationTemplate,
            From = new EmailAccount
            {
                Email = string.IsNullOrWhiteSpace(configuration.EmailConfig.FromEmail)
                    ? configuration.EmailConfig.EmailAccount
                    : configuration.EmailConfig.FromEmail
            },
            To = [new EmailAccount { Email = user.Email!, Name = user.Name }],
            Variables =
            {
                { "Name", user.Name },
                { "InvitedByName", string.IsNullOrWhiteSpace(invitedByName) ? "An administrator" : invitedByName },
                { "SetupPasswordUrl", setupUrl },
                { "WebsiteUrl", website },
                { "LoginUrl", $"{website}/login" },
                { "SupportEmail", configuration.EmailConfig.SupportEmail }
            }
        }, token);
    }

    private static void EnsureMayGrant(bool administrator, bool callerIsSystemAdministrator)
    {
        if (administrator && !callerIsSystemAdministrator)
            throw new UnauthorizedAccessException("Only system administrators can create administrators.");
    }

    private static string NormalizeEmail(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        try
        {
            if (trimmed.Length is > 0 and <= 256 && new MailAddress(trimmed).Address == trimmed) return trimmed;
        }
        catch (FormatException)
        {
        }
        throw new ArgumentException("Enter a valid email address.", nameof(value));
    }
}
