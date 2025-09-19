using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutomatonCore(this IServiceCollection services)
    {
        // Register factory function for FlowExecuteService with fallback application name
        // Applications can override this registration with their specific application name if needed
        services.AddTransient<FlowExecuteService>(provider =>
        {
            var flowConvertService = provider.GetRequiredService<FlowConvertService>();
            var notificationService = provider.GetService<IExecutionNotificationService>();
            var persistenceService = provider.GetService<IWorkflowExecutionPersistenceService>();

            // Use a generic fallback name - specific applications should override this registration
            // if they need a specific application name (like the Console Runner does)
            return new FlowExecuteService(flowConvertService, "Automaton.Application", notificationService, persistenceService);
        });

        services.AddTransient<FlowConvertService>(); // Keep concrete for backwards compatibility
        services.AddTransient<IFlowConvertService<Flow>, FlowConvertService>(); // Add interface registration

        return services;
    }
}

