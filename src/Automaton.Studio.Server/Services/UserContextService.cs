using System.Security.Claims;
using Serilog;

namespace Automaton.Studio.Server.Services;

public class UserContextService
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly Serilog.ILogger logger;

    public UserContextService(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.logger = Log.ForContext<UserContextService>();
    }

    public Guid GetUserId()
    {
        var userIdString = httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (Guid.TryParse(userIdString, out Guid userId))
        {
            logger.Debug("Retrieved user ID from claims: {UserId} (Authentication: {AuthenticationType})", 
                userId, httpContextAccessor.HttpContext?.User?.Identity?.AuthenticationType);
            return userId;
        }

        logger.Debug("No valid user ID found in claims. Available claims: {Claims}", 
            string.Join(", ", httpContextAccessor.HttpContext?.User?.Claims?.Select(c => $"{c.Type}={c.Value}") ?? new string[0]));
        
        return Guid.Empty;
    }

    public string GetUserName()
    {
        var userName = httpContextAccessor.HttpContext?.User?.Identity?.Name;

        return userName ?? string.Empty;
    }
}
