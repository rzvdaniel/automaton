using Automaton.Api.Application.Commands;
using Automaton.Api.Authentication;
using MediatR;

namespace Automaton.Api.Application.Queries;

public class SignInUserQuery : SignInUserCommand, IRequest<JsonWebToken>
{
    public int RefreshTokenExpirationDays { get; }

    public SignInUserQuery(int refreshTokenExpirationDays, string userName, string password)
        : base(userName, password)
    {
        RefreshTokenExpirationDays = refreshTokenExpirationDays;
    }
}