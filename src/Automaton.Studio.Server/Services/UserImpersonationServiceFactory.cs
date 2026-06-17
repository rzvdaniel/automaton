using Serilog;

namespace Automaton.Api.Services;

/// <summary>
/// Generic factory for creating service instances scoped to specific users via impersonation.
/// This enables web apps to execute operations as the app owner rather than the viewer.
/// 
/// Usage:
///   var factory = new UserImpersonationServiceFactory&lt;FlowsService&gt;(serviceProvider);
///   var ownerService = factory.CreateForUser(ownerId);
/// </summary>
/// <typeparam name="TService">The service type to create with impersonation</typeparam>
public class UserImpersonationServiceFactory<TService> where TService : class
{
    private readonly IServiceProvider serviceProvider;
    private readonly Serilog.ILogger logger;

    public UserImpersonationServiceFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.logger = Log.ForContext<UserImpersonationServiceFactory<TService>>();
    }

    /// <summary>
    /// Create a service instance scoped to a specific user (for impersonation).
    /// Used by web apps to execute operations with the app/flow owner's permissions.
    /// 
    /// NOTE: The returned service uses the controller's scope, not a new scope.
    /// This ensures the DbContext stays alive for the entire request.
    /// </summary>
    /// <param name="userId">The user ID to impersonate</param>
    /// <returns>Service instance operating under the impersonated user's context</returns>
    public TService CreateForUser(Guid userId)
    {
        logger.Debug("Creating {ServiceType} for user: {UserId}", typeof(TService).Name, userId);

        // Get UserContextService from current request scope
        var userContextService = serviceProvider.GetRequiredService<UserContextService>();

        // Set impersonation context
        userContextService.ImpersonateUser(userId);

        // Create service with impersonated context
        // This will use the same scope as the controller, so DbContext stays alive
        var service = serviceProvider.GetRequiredService<TService>();

        logger.Debug("{ServiceType} created for user: {UserId}", typeof(TService).Name, userId);

        return service;
    }
}
