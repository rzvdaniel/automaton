using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Common.Authentication;
using MediatR;
using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Application.Queries.Handlers
{
    public class SignInUserQueryHandler : IRequestHandler<SignInUserQuery, JsonWebToken>
    {
        private readonly ApplicationDbContext dataContext;
        private readonly UsersService userManagerService;
        private readonly IJwtService jwtService;
        private readonly ILogger<SignInUserQueryHandler> logger;

        public SignInUserQueryHandler(
            ApplicationDbContext dataContext, 
            UsersService userManagerService, 
            IJwtService jwtService,
            ILogger<SignInUserQueryHandler> logger)
        {
            this.dataContext = dataContext;
            this.userManagerService = userManagerService;
            this.jwtService = jwtService;
            this.logger = logger;
        }

        public async Task<JsonWebToken> Handle(SignInUserQuery command, CancellationToken cancellationToken)
        {
            var user = await userManagerService.GetUserByEmailOrUserName(command.Email);

            if (user == null || await userManagerService.ValidatePasswordAsync(user, command.Password) == false)
            {
                throw new Exception("Invalid credentials.");
            }

            var refreshToken = new RefreshToken(user.Id, command.RefreshTokenExpirationDays);
            var roles = (await userManagerService.GetRoles(user.Id)).ToImmutableList();
            var jwt = jwtService.GenerateToken(user.Id.ToString(), user.Email, roles, GetCustomClaimsForUser(user.Id));

            jwt.RefreshToken = refreshToken.Token;

            await dataContext.Set<RefreshToken>().AddAsync(refreshToken, cancellationToken);
            await dataContext.SaveChangesAsync(cancellationToken);

            return jwt;
        }

        private IDictionary<string, string> GetCustomClaimsForUser(Guid userId)
        {
            //Add custom claims here
            return new Dictionary<string, string>();
        }
    }
}