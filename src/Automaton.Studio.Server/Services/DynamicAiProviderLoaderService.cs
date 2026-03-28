using Automaton.Mcp.Client.Providers;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Startup service that auto-registers any OpenAI-compatible provider stored in the database.
/// Admins can add new providers (with a BaseUrl) via the admin UI and they become available
/// immediately after the next server restart — no code changes required.
/// </summary>
public sealed class DynamicAiProviderLoaderService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiProviderRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DynamicAiProviderLoaderService> _logger;

    public DynamicAiProviderLoaderService(
        IServiceScopeFactory scopeFactory,
        IAiProviderRegistry registry,
        ILoggerFactory loggerFactory,
        ILogger<DynamicAiProviderLoaderService> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var providers = await dbContext.AiProviders
            .Where(p => p.IsActive && p.BaseUrl != null)
            .Include(p => p.Models.Where(m => m.IsActive))
            .OrderBy(p => p.SortOrder)
            .ToListAsync(cancellationToken);

        foreach (var provider in providers)
        {
            if (_registry.IsImplemented(provider.Name))
            {
                _logger.LogDebug("Skipping dynamic registration for '{Provider}' — already registered in code.", provider.Name);
                continue;
            }

            var defaultModel = provider.Models
                .OrderByDescending(m => m.IsDefault)
                .ThenBy(m => m.SortOrder)
                .Select(m => m.ModelId)
                .FirstOrDefault();

            if (defaultModel is null)
            {
                _logger.LogWarning(
                    "Skipping dynamic registration for '{Provider}' — no active models found in database.",
                    provider.Name);
                continue;
            }

            var instance = new OpenAiCompatibleProvider(
                providerName: provider.Name,
                baseUrl: provider.BaseUrl!,
                defaultModel: defaultModel,
                logger: _loggerFactory.CreateLogger($"{provider.Name}Provider"));

            _registry.Register(instance);

            _logger.LogInformation(
                "Dynamically registered OpenAI-compatible provider '{Provider}' → {BaseUrl} (default model: {Model}).",
                provider.Name, provider.BaseUrl, defaultModel);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
