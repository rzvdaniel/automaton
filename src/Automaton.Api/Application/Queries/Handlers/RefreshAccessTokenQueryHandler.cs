using Automaton.Api.Authentication;
using Automaton.Api.Data;
using Automaton.Api.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;

namespace Automaton.Api.Application.Queries.Handlers
{
    public class RefreshAccessTokenQueryHandler : IRequestHandler<RefreshAccessTokenQuery, JsonWebToken>
    {
        private readonly ApplicationDbContext dataContext;
        private readonly IJwtService jwtService;
        private readonly UsersService userManagerService;
        private readonly ILogger<RefreshAccessTokenQueryHandler> logger;

        public RefreshAccessTokenQueryHandler
        (
            ApplicationDbContext dataContext, 
            IJwtService jwtService,
            UsersService userManagerService, 
            ILogger<RefreshAccessTokenQueryHandler> logger
        )
        {
            this.dataContext = dataContext;
            this.jwtService = jwtService;
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
                throw new InvalidOperationException("Refresh token not found.");
            }

            refreshToken.ValidateRefreshToken();

            var user = await userManagerService.GetUserById(refreshToken.UserId);

            if (user == null)
            {
                throw new InvalidOperationException($"User: '{refreshToken.UserId}' not found.");
            }

            var newRefreshToken = new RefreshToken(user.Id, request.RefreshTokenExpirationDays);

            await ReplaceRefreshTokenAsync(refreshToken, newRefreshToken, user.Id, cancellationToken);

            var claims = GetCustomClaimsForUser();
            var roles = (await userManagerService.GetRoles(user.Id)).ToImmutableList();
            var userName = user.UserName
                ?? throw new InvalidOperationException($"User {user.Id} has no username for token issuance.");
            var jwt = jwtService.GenerateToken(user.Id.ToString("N"), userName, roles, claims);
            jwt.RefreshToken = newRefreshToken.Token;

            logger.Log(LogLevel.Debug, "AccessTokenRefreshed Event Published.");

            return jwt;
        }

        static Dictionary<string, string> GetCustomClaimsForUser()
        {
            //Get Custom Claims for User
            return new Dictionary<string, string>();
        }

        private async Task ReplaceRefreshTokenAsync(
            RefreshToken currentToken,
            RefreshToken replacementToken,
            Guid userId,
            CancellationToken cancellationToken)
        {
            await using var transaction = await dataContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                dataContext.Entry(currentToken).State = EntityState.Deleted;
                await dataContext.Set<RefreshToken>().AddAsync(replacementToken, cancellationToken);
                await dataContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                logger.LogWarning(
                    ex,
                    "Concurrency issue refreshing token for user {UserId}. The token may have been already refreshed or revoked elsewhere.",
                    userId);
                throw new InvalidOperationException("Refresh token refresh operation failed. Please try again.", ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
