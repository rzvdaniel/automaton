using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Factory for creating FlowsService instances scoped to specific users.
/// This enables web apps to execute flows as the app owner rather than the viewer.
/// </summary>
public class FlowsServiceFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly Serilog.ILogger logger;

    public FlowsServiceFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.logger = Log.ForContext<FlowsServiceFactory>();
    }

    /// <summary>
    /// Create a FlowsService scoped to a specific user (for impersonation).
    /// Used by web apps to execute flows with the app owner's permissions.
    /// 
    /// NOTE: The returned service uses the controller's scope, not a new scope.
    /// This ensures the DbContext stays alive for the entire request.
    /// </summary>
    public FlowsService CreateForUser(Guid userId)
    {
        logger.Debug("Creating FlowsService for user: {UserId}", userId);

        // ? FIX: Don't create a new scope - use the current request scope
        // The controller's scope will handle disposal at the end of the request
        var userContextService = serviceProvider.GetRequiredService<UserContextService>();

        // Set impersonation context
        userContextService.ImpersonateUser(userId);

        // Create FlowsService with impersonated context
        // This will use the same scope as the controller, so DbContext stays alive
        var flowsService = serviceProvider.GetRequiredService<FlowsService>();

        logger.Debug("FlowsService created for user: {UserId}", userId);

        return flowsService;
    }
}
