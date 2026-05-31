using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace Automaton.Studio.Server.Application.Commands.Handlers
{
    public class ForgotUserPasswordHandler : IRequestHandler<ForgotUserPasswordCommand>
    {
        private ConfigurationService ConfigurationService { get; set; }
        private UserManager<ApplicationUser> UserManager { get; set; }
        private IEmailService EmailService { get; set; }
        private ILogger<ForgotUserPasswordHandler> Logger { get; set; }

        public ForgotUserPasswordHandler(
            ConfigurationService configurationService,
            IEmailService emailService,
            UserManager<ApplicationUser> userManager,
            ILogger<ForgotUserPasswordHandler> logger)
        {
            ConfigurationService = configurationService;
            UserManager = userManager;
            EmailService = emailService;
            Logger = logger;
        }

        public async Task Handle(ForgotUserPasswordCommand command, CancellationToken cancellationToken)
        {
            var user = await UserManager.FindByEmailAsync(command.Email);

            if (user is null)
            {
                Logger.LogInformation("Forgot password requested for non-existent email {Email}", command.Email);
                return;
            }
            
            var code = await UserManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            var resetPasswordUrl = $"{ConfigurationService.WebsiteUrl}/reset-password?email={command.Email}&code={code}";

            var userDetails = new EmailData
            {
                Template = "AutomatonResetPassword",
                From = new EmailAccount
                {
                    Email = string.IsNullOrWhiteSpace(ConfigurationService.EmailConfig.FromEmail)
                        ? ConfigurationService.EmailConfig.EmailAccount
                        : ConfigurationService.EmailConfig.FromEmail
                },
                To = [new EmailAccount { Email = command.Email, Name = user.Name }],
                Variables =
                {
                    { "WebsiteUrl", ConfigurationService.WebsiteUrl},
                    { "SupportEmail", ConfigurationService.EmailConfig.SupportEmail!},
                    { "Name", user.Name },
                    { "ResetPasswordUrl", resetPasswordUrl },
                }
            };

            await EmailService.Send(userDetails, CancellationToken.None);
            Logger.LogInformation("Password reset email sent to {Email}", command.Email);
        }
    }
}
