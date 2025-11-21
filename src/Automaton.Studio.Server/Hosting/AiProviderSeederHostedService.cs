using Automaton.Studio.Server.Services;

namespace Automaton.Studio.Server.Hosting;

/// <summary>
/// Background service that seeds AI providers on application startup
/// </summary>
public class AiProviderSeederHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AiProviderSeederHostedService> _logger;

    public AiProviderSeederHostedService(
        IServiceProvider serviceProvider,
        ILogger<AiProviderSeederHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting AI provider seeder...");

            using var scope = _serviceProvider.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<AiProviderSeeder>();
            
            await seeder.SeedProvidersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running AI provider seeder");
            // Don't throw - allow application to start even if seeding fails
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
