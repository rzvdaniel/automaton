using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutomatonCore(this IServiceCollection services)
    {
        // Register factory function for FlowExecuteService that can optionally use persistence
        services.AddTransient<FlowExecuteService>(provider =>
        {
            var flowConvertService = provider.GetRequiredService<FlowConvertService>();
            var notificationService = provider.GetService<IExecutionNotificationService>();
            var persistenceService = provider.GetService<IWorkflowExecutionPersistenceService>();

            return new FlowExecuteService(flowConvertService, notificationService, persistenceService);
        });

        services.AddTransient<FlowConvertService>(); // Keep concrete for backwards compatibility
        services.AddTransient<IFlowConvertService<Flow>, FlowConvertService>(); // Add interface registration

        return services;
    }
}

