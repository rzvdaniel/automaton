using Automaton.Api.Application.Commands;
using Automaton.Api.Authentication;
using MediatR;

namespace Automaton.Api.Application.Queries
{
    public class RefreshAccessTokenQuery : RefreshAccessTokenCommand, IRequest<JsonWebToken>
    {
        public int RefreshTokenExpirationDays { get; }

        public RefreshAccessTokenQuery(int refreshTokenExpirationDays, string token)
            : base(token)
        {
            RefreshTokenExpirationDays = refreshTokenExpirationDays;
        }
    }
}