using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using MediatR;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace Automaton.Studio.Server.Application.Commands.Handlers
{
    public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand>
    {
        private UsersService UserManagerService { get; set; }
        private ConfigurationService ConfigurationService { get; set; }
        private IEmailService EmailService { get; set; }
        private ILogger<ResetUserPasswordCommandHandler> Logger { get; set; }

        public ResetUserPasswordCommandHandler(UsersService userManager, ConfigurationService configurationService, 
            IEmailService emailService, ILogger<ResetUserPasswordCommandHandler> logger)
        {
            UserManagerService = userManager;
            ConfigurationService = configurationService;
            EmailService = emailService;
            Logger = logger;
        }

        public async Task Handle(ResetUserPasswordCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var user = await UserManagerService.GetUserByEmailOrUserName(command.Email);

                if (user is null)
                {
                    return;
                }

                var decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Code));

                await UserManagerService.ResetPasswordAsync(user.Id, decodedCode, command.Password);

                var userDetails = new EmailData
                {
                    Template = "AutomatonResetPasswordConfirmation",
                    From = new EmailAccount { Email = ConfigurationService.EmailConfig.EmailSupport },
                    To = [new EmailAccount { Email = command.Email, Name = user.Name }],
                    Variables =
                    {
                        { "WebsiteUrl", ConfigurationService.WebsiteUrl},
                        { "SupportEmail", ConfigurationService.EmailConfig.EmailSupport!},
                        { "Name", user.Name },
                    }
                };

                await EmailService.Send(userDetails, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Debug, "Could not reset user password.");
                throw;
            }       
        }
    }
}