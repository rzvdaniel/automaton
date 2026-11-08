using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using MediatR;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Text;

namespace Automaton.Api.Application.Commands.Handlers
{
    public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand>
    {
        private UsersService UserManagerService { get; set; }
        private ConfigurationService ConfigurationService { get; set; }
        private IEmailService EmailService { get; set; }
        private ILogger<ResetUserPasswordCommandHandler> Logger { get; set; }
        private IDataProtectionProvider DataProtection { get; set; }
        private DataProtectionTokenProviderOptions TokenOptions { get; set; }

        public ResetUserPasswordCommandHandler(UsersService userManager, ConfigurationService configurationService, 
            IEmailService emailService, ILogger<ResetUserPasswordCommandHandler> logger,
            IDataProtectionProvider dataProtection, IOptions<DataProtectionTokenProviderOptions> tokenOptions)
        {
            DataProtection = dataProtection;
            TokenOptions = tokenOptions.Value;
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

                // An unknown email gets the same answer as a bad link, so the response never reveals accounts.
                var decoded = TryDecode(command.Code, out var decodedCode);
                if (user is null || !decoded)
                {
                    throw new InvalidResetLinkException(ClassifyLink(decodedCode));
                }

                try
                {
                    await UserManagerService.ResetPasswordAsync(user.Id, decodedCode, command.Password);
                }
                catch (InvalidResetLinkException)
                {
                    throw new InvalidResetLinkException(ClassifyLink(decodedCode));
                }
                await UserManagerService.RevokeAllRefreshTokensAsync(user.Id, cancellationToken);

                var userDetails = new EmailData
                {
                    Template = "AutomatonResetPasswordConfirmation",
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
                        { "SupportEmail", ConfigurationService.EmailConfig.SupportEmail},
                        { "Name", user.Name },
                    }
                };

                await EmailService.Send(userDetails, CancellationToken.None);
            }
            catch (InvalidResetLinkException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Could not reset user password.");
                throw;
            }       
        }

        private static bool TryDecode(string code, out string decoded)
        {
            try
            {
                decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
                return true;
            }
            catch (FormatException)
            {
                decoded = string.Empty;
                return false;
            }
        }

        private string ClassifyLink(string code) =>
            ResetLinkClassifier.Classify(DataProtection, TokenOptions, code, DateTimeOffset.UtcNow);
    }
}
