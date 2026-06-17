using MediatR;

namespace Automaton.Api.Application.Commands
{
    public class RevokeRefreshTokenCommand : IRequest
    {
        public Guid UserId { get; set; }
        public string? Token { get; set; }

        public RevokeRefreshTokenCommand()
        {
        }

        public RevokeRefreshTokenCommand(Guid userId, string token)
        {
            UserId = userId;
            Token = token;
        }
    }
}
