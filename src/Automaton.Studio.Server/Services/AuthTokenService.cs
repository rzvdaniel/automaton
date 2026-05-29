using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Common.Authentication;
using System.Collections.Immutable;

namespace Automaton.Studio.Server.Services;

public class AuthTokenService
{
    private readonly ApplicationDbContext _dataContext;
    private readonly IJwtService _jwtService;
    private readonly UsersService _usersService;

    public AuthTokenService(
        ApplicationDbContext dataContext,
        IJwtService jwtService,
        UsersService usersService)
    {
        _dataContext = dataContext;
        _jwtService = jwtService;
        _usersService = usersService;
    }

    public async Task<JsonWebToken> IssueForUserAsync(ApplicationUser user, int refreshTokenLifetimeDays, CancellationToken cancellationToken)
    {
        var refreshToken = new RefreshToken(user.Id, refreshTokenLifetimeDays);
        var roles = (await _usersService.GetRoles(user.Id)).ToImmutableList();
        var jwt = _jwtService.GenerateToken(user.Id.ToString(), user.Email!, roles, GetCustomClaimsForUser(user.Id));

        jwt.RefreshToken = refreshToken.Token;

        await _dataContext.Set<RefreshToken>().AddAsync(refreshToken, cancellationToken);
        await _dataContext.SaveChangesAsync(cancellationToken);

        return jwt;
    }

    private static IDictionary<string, string> GetCustomClaimsForUser(Guid userId)
    {
        return new Dictionary<string, string>();
    }
}
