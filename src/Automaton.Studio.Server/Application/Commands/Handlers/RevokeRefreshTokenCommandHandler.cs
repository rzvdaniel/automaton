using Automaton.Studio.Server.Core.Commands;
using Automaton.Studio.Server.Data;
using Common.Authentication;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Application.Commands.Handlers
{
    public class RevokeRefreshTokenCommandHandler : IRequestHandler<RevokeRefreshTokenCommand>
    {
        private readonly ApplicationDbContext dataContext;
        private readonly ILogger<RevokeRefreshTokenCommandHandler> logger;

        public RevokeRefreshTokenCommandHandler(ApplicationDbContext dataContext,
            ILogger<RevokeRefreshTokenCommandHandler> logger)
        {
            this.dataContext = dataContext;
            this.logger = logger;
        }

        public async Task Handle(RevokeRefreshTokenCommand request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return;
            }

            var refreshTokens = dataContext.Set<RefreshToken>().Where(x => x.Token == request.Token);
            if (request.UserId != Guid.Empty)
            {
                refreshTokens = refreshTokens.Where(x => x.UserId == request.UserId);
            }

            var refreshToken = await refreshTokens.SingleOrDefaultAsync(cancellationToken);

            if (refreshToken == null)
            {
                return;
            }

            refreshToken.Revoke();
            dataContext.Set<RefreshToken>().Update(refreshToken);
            await dataContext.SaveChangesAsync(cancellationToken);
            
            logger.Log(LogLevel.Debug, "RefreshTokenRevoked Event Published.");
        }
    }
}
