using Automaton.Api.Authentication;
using MediatR;

namespace Automaton.Api.Application.Commands.Handlers
{
    public class RevokeAccessTokenCommandHandler : IRequestHandler<RevokeAccessTokenCommand>
    {
        private readonly ILogger<RevokeAccessTokenCommandHandler> logger;
        private readonly IAccessTokenManagerService accessTokenManagerService;

        public RevokeAccessTokenCommandHandler(ILogger<RevokeAccessTokenCommandHandler> logger,
            IAccessTokenManagerService accessTokenManagerService)
        {
            this.logger = logger;
            this.accessTokenManagerService = accessTokenManagerService;
        }

        public async Task Handle(RevokeAccessTokenCommand request, CancellationToken cancellationToken)
        {
            await accessTokenManagerService.DeactivateAccessTokenAsync(request.AccessToken);

            logger.Log(LogLevel.Debug, "AccessTokenRevoked Event Published.");
        }
    }
}