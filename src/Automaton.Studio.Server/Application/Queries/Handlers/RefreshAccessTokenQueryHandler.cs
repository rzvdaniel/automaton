using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Common.Authentication;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;

namespace Automaton.Studio.Server.Application.Queries.Handlers
{
    public class RefreshAccessTokenQueryHandler : IRequestHandler<RefreshAccessTokenQuery, JsonWebToken>
    {
        private readonly ApplicationDbContext dataContext;
        private readonly IJwtService jwtService;
        private readonly IMediator mediator;
        private readonly UsersService userManagerService;
        private readonly ILogger<RefreshAccessTokenQueryHandler> logger;

        public RefreshAccessTokenQueryHandler
        (
            ApplicationDbContext dataContext, 
            IJwtService jwtService, IMediator mediator,
            UsersService userManagerService, 
            ILogger<RefreshAccessTokenQueryHandler> logger
        )
        {
            this.dataContext = dataContext;
            this.jwtService = jwtService;
            this.mediator = mediator;
            this.userManagerService = userManagerService;
            this.logger = logger;
        }

        public async Task<JsonWebToken> Handle(RefreshAccessTokenQuery request, CancellationToken cancellationToken)
        {
            var refreshToken = await dataContext
                .Set<RefreshToken>()
                .AsTracking()
                .SingleOrDefaultAsync(x => x.Token == request.Token, cancellationToken);

            if (refreshToken == null)
            {
                throw new Exception("Refresh token not found.");
            }

            refreshToken.ValidateRefreshToken();

            var user = await userManagerService.GetUserById(refreshToken.UserId);

            if (user == null)
            {
                throw new Exception($"User: '{refreshToken.UserId}' not found.");
            }

            var newRefreshToken = new RefreshToken(user.Id, request.RefreshTokenExpirationDays);

            try
            {
                dataContext.BeginTransaction();
                
                // Detach the old token and remove it
                dataContext.Entry(refreshToken).State = EntityState.Deleted;
                
                // Add the new token
                await dataContext.Set<RefreshToken>().AddAsync(newRefreshToken, cancellationToken);
                
                // SaveChanges instead of SaveChangesAsync to respect the transaction
                await dataContext.SaveChangesAsync(cancellationToken);
                
                dataContext.Commit();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                dataContext.Rollback();
                logger.LogWarning($"Concurrency issue refreshing token for user {user.Id}. The token may have been already refreshed or revoked elsewhere.");
                throw new Exception("Refresh token refresh operation failed. Please try again.", ex);
            }
            catch
            {
                dataContext.Rollback();
                throw;
            }

            var claims = GetCustomClaimsForUser(user.Id);
            var roles = (await userManagerService.GetRoles(user.Id)).ToImmutableList();
            var jwt = jwtService.GenerateToken(user.Id.ToString("N"), user.UserName, roles, claims);
            jwt.RefreshToken = newRefreshToken.Token;

            logger.Log(LogLevel.Debug, "AccessTokenRefreshed Event Published.");

            return jwt;
        }

        Dictionary<string, string> GetCustomClaimsForUser(Guid userId)
        {
            //Get Custom Claims for User
            return new Dictionary<string, string>();
        }
    }
}