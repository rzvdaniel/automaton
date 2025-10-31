using System.Security.Claims;
using Serilog;

namespace Automaton.Studio.Server.Services;

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
    /// Create a scoped context for executing operations as a different user (impersonation).
    /// Used for web apps to execute flows as the app owner rather than the viewer.
    /// </summary>
    public IDisposable ImpersonateUser(Guid userId)
    {
        logger.Debug("Starting user impersonation: {UserId}", userId);
        impersonatedUserId = userId;
        return new ImpersonationToken(() =>
        {
            logger.Debug("Ending user impersonation: {UserId}", userId);
            impersonatedUserId = null;
        });
    }

    private class ImpersonationToken : IDisposable
    {
        private readonly Action onDispose;
        private bool disposed = false;

        public ImpersonationToken(Action onDispose)
        {
            this.onDispose = onDispose;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                onDispose();
                disposed = true;
            }
        }
    }
}
