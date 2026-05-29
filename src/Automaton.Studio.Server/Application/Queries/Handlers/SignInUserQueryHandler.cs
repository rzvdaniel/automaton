using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Services;
using Common.Authentication;
using MediatR;

namespace Automaton.Studio.Server.Application.Queries.Handlers
{
    public class SignInUserQueryHandler : IRequestHandler<SignInUserQuery, JsonWebToken>
    {
        private readonly UsersService userManagerService;
        private readonly AuthTokenService authTokenService;

        public SignInUserQueryHandler(
            UsersService userManagerService, 
            AuthTokenService authTokenService)
        {
            this.userManagerService = userManagerService;
            this.authTokenService = authTokenService;
        }

        public async Task<JsonWebToken> Handle(SignInUserQuery command, CancellationToken cancellationToken)
        {
            var user = await userManagerService.GetUserByEmailOrUserName(command.Email);

            if (user == null || await userManagerService.ValidatePasswordAsync(user, command.Password) == false)
            {
                throw new Exception("Invalid credentials.");
            }

            return await authTokenService.IssueForUserAsync(user, command.RefreshTokenExpirationDays, cancellationToken);
        }
    }
}
