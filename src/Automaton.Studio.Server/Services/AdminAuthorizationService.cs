using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Services;

public class AdminAuthorizationService
{
    private readonly IHttpContextAccessor httpContextAccessor;

    public AdminAuthorizationService(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    public void EnsureAdmin()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("Authentication is required.");
        }

        if (!user.IsInRole(UserRole.Administrator) && !user.IsInRole(UserRole.SystemAdministrator))
        {
            throw new UnauthorizedAccessException("Administrator access is required.");
        }
    }
}
