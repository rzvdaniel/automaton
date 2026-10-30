namespace Automaton.Api.Tests;

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
