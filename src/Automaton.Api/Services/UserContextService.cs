using System.Security.Claims;
using Serilog;

namespace Automaton.Api.Services;

public class UserContextService
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly Serilog.ILogger logger;
    private Guid? impersonatedUserId; // For service impersonation

    public UserContextService(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.logger = Log.ForContext<UserContextService>();
    }

    public Guid GetUserId()
    {
        // Return impersonated user if set (for web app flow execution as owner)
        if (impersonatedUserId.HasValue)
        {
            logger.Debug("Using impersonated user ID: {UserId}", impersonatedUserId.Value);
            return impersonatedUserId.Value;
        }

        // Otherwise, return authenticated user from HTTP context
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

    /// <summary>
    /// Set impersonation context for the current request.
    /// Used for web apps to execute flows as the app owner rather than the viewer.
    /// Impersonation is automatically cleared at the end of the request scope.
    /// </summary>
    public void ImpersonateUser(Guid userId)
    {
        logger.Debug("Setting user impersonation: {UserId}", userId);
        impersonatedUserId = userId;
    }

    /// <summary>
    /// Clear impersonation context (restore to authenticated user).
    /// </summary>
    public void ClearImpersonation()
    {
        logger.Debug("Clearing user impersonation");
        impersonatedUserId = null;
    }
}
