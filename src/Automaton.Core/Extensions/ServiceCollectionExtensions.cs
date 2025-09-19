using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutomatonCore(this IServiceCollection services)
    {
        services.AddTransient<FlowExecuteService>();
        services.AddTransient<FlowConvertService>(); // Keep concrete for backwards compatibility
        services.AddTransient<IFlowConvertService<Flow>, FlowConvertService>(); // Add interface registration

        return services;
    }
}

