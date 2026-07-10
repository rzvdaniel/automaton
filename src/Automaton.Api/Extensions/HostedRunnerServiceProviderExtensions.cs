using Automaton.Api.Services;

namespace Automaton.Api.Extensions;

public static class HostedRunnerServiceProviderExtensions
{
    public static async Task SeedLocalRunnerHostAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var bootstrapService = scope.ServiceProvider.GetRequiredService<LocalRunnerHostBootstrapService>();
        await bootstrapService.SeedAsync(cancellationToken);
    }
}
