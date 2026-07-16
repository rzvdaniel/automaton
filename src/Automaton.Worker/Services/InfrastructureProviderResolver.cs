using Automaton.Infrastructure.Providers;

namespace Automaton.Worker.Services;

public interface IInfrastructureProviderResolver
{
    IRunnerHostInfrastructureProvider Resolve(string providerName);
}

public sealed class InfrastructureProviderResolver(
    IEnumerable<IRunnerHostInfrastructureProvider> providers) : IInfrastructureProviderResolver
{
    public IRunnerHostInfrastructureProvider Resolve(string providerName)
    {
        var matches = providers
            .Where(x => string.Equals(x.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No infrastructure provider is registered for '{providerName}'."),
            _ => throw new InvalidOperationException($"Multiple infrastructure providers are registered for '{providerName}'.")
        };
    }
}
