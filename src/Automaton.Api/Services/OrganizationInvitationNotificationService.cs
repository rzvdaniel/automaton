using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class OrganizationInvitationNotificationService(
    ApplicationDbContext dbContext,
    IEmailService emailService,
    ConfigurationService configurationService,
    UserManager<ApplicationUser> userManager,
    UsersService usersService)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<OrganizationInvitationNotificationService>();

    public async Task SendAsync(
        OrganizationInvitation invitation,
        Organization organization,
        InviteMemberRequest request,
        Guid inviterUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var invitee = await GetOrCreateInviteeAsync(request.Email, cancellationToken);
            var setupPasswordUrl = await CreatePasswordSetupUrlAsync(invitee);
            var inviterName = await GetInviterNameAsync(inviterUserId, cancellationToken);
            var email = CreateEmail(invitation, organization, request, invitee.User, inviterName, setupPasswordUrl);

            await emailService.Send(email, cancellationToken);
            logger.Debug(
                "Invitation email sent to {Email} for organization {OrganizationId}",
                request.Email,
                organization.Id);
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Failed to send invitation email to {Email} for organization {OrganizationId}",
                request.Email,
                organization.Id);
            // Invitation creation succeeds even if email delivery fails.
        }
    }

    private async Task<InviteeAccount> GetOrCreateInviteeAsync(string email, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user is not null)
        {
            return new InviteeAccount(user, CreatedNewUser: false);
        }

        var userName = email.Split('@')[0];
        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            Name = userName,
            PublicUsername = await usersService.GenerateUniquePublicUsernameAsync(userName)
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            logger.Warning(
                "Failed to create user account for {Email}: {Errors}",
                email,
                string.Join(", ", createResult.Errors.Select(error => error.Description)));
            return new InviteeAccount(user, CreatedNewUser: false);
        }

        logger.Debug("Created new user account for organization invitation: {Email}", email);
        return new InviteeAccount(user, CreatedNewUser: true);
    }

    private async Task<string?> CreatePasswordSetupUrlAsync(InviteeAccount invitee)
    {
        if (!invitee.CreatedNewUser && await userManager.HasPasswordAsync(invitee.User))
        {
            return null;
        }

        var rawResetToken = await userManager.GeneratePasswordResetTokenAsync(invitee.User);
        var encodedResetToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawResetToken));
        var email = invitee.User.Email!;
        return $"{configurationService.WebsiteUrl.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(email)}&code={encodedResetToken}&create=true";
    }

    private async Task<string> GetInviterNameAsync(Guid inviterUserId, CancellationToken cancellationToken)
    {
        var inviter = await dbContext.Users.FirstOrDefaultAsync(user => user.Id == inviterUserId, cancellationToken);
        return inviter?.Name ?? inviter?.UserName ?? "Someone";
    }

    private EmailData CreateEmail(
        OrganizationInvitation invitation,
        Organization organization,
        InviteMemberRequest request,
        ApplicationUser invitee,
        string inviterName,
        string? setupPasswordUrl)
    {
        var invitationUrl = $"{configurationService.WebsiteUrl.TrimEnd('/')}/organizations/invitations/accept?token={invitation.Token}";
        var expirationHours = (int)Math.Ceiling((invitation.ExpiresAt - DateTime.UtcNow).TotalHours);
        var variables = new Dictionary<string, string>
        {
            ["OrganizationName"] = organization.Name,
            ["InviterName"] = inviterName,
            ["Role"] = request.Role.ToString(),
            ["ExpirationHours"] = expirationHours.ToString(),
            ["SupportEmail"] = configurationService.EmailConfig.SupportEmail
        };

        if (!string.IsNullOrEmpty(setupPasswordUrl))
        {
            variables["SetupPasswordUrl"] = setupPasswordUrl;
        }
        else
        {
            variables["InvitationUrl"] = invitationUrl;
        }

        return new EmailData
        {
            Template = "AutomatonOrganizationInvitation",
            From = new EmailAccount
            {
                Email = string.IsNullOrWhiteSpace(configurationService.EmailConfig.FromEmail)
                    ? configurationService.EmailConfig.EmailAccount
                    : configurationService.EmailConfig.FromEmail
            },
            To = new[] { new EmailAccount { Email = request.Email, Name = invitee.Name ?? request.Email } },
            Variables = variables
        };
    }

    private sealed record InviteeAccount(ApplicationUser User, bool CreatedNewUser);
}
