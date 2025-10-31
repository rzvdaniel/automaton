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
    /// </summary>
    public FlowsService CreateForUser(Guid userId)
    {
        logger.Debug("Creating FlowsService for user: {UserId}", userId);

        using var scope = serviceProvider.CreateScope();

        var userContextService = scope.ServiceProvider.GetRequiredService<UserContextService>();

        // Set impersonation context
        using var impersonation = userContextService.ImpersonateUser(userId);

        // Create FlowsService with impersonated context
        var flowsService = scope.ServiceProvider.GetRequiredService<FlowsService>();

        logger.Debug("FlowsService created for user: {UserId}", userId);

        return flowsService;
    }
}
