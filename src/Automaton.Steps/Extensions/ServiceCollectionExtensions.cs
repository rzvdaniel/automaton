using Automaton.Steps;
using Automaton.Steps.Config;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static void AddAutomatonSteps(this IServiceCollection services)
    {
        services.AddTransient<EmitLog>();
        services.AddTransient<ExecutePython>();
        services.AddTransient<ExecuteFlow>();
        services.AddTransient<Sequence>();
        services.AddTransient<SequenceEnd>();
        services.AddTransient<If>();
        services.AddTransient<CustomStep>();
        services.AddTransient<ConfigService>();  
    }
}
