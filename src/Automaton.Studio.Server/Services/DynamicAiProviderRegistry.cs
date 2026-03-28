using System.Collections.Concurrent;
using Automaton.Mcp.Client;
using Automaton.Mcp.Client.Providers;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Wraps the hardcoded <see cref="AiProviderRegistry"/> with on-demand DB lookup.
/// When a provider name is not found in the in-memory registry, this class queries the
/// <c>AiProviders</c> table for an active entry with a <c>BaseUrl</c> and instantiates an
/// <see cref="OpenAiCompatibleProvider"/> on the fly — no server restart required.
/// The resolved provider is cached so subsequent calls pay no DB cost.
/// </summary>
public sealed class DynamicAiProviderRegistry : IAiProviderRegistry
{
    private readonly AiProviderRegistry _hardcoded;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DynamicAiProviderRegistry> _logger;

    /// <summary>Thread-safe cache of dynamically loaded providers.</summary>
    private readonly ConcurrentDictionary<string, IAiProvider> _dynamic =
        new(StringComparer.OrdinalIgnoreCase);

    public DynamicAiProviderRegistry(
        AiProviderRegistry hardcoded,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        ILogger<DynamicAiProviderRegistry> logger)
    {
        _hardcoded = hardcoded;
        _scopeFactory = scopeFactory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IAiProvider> Providers
    {
        get
        {
            // Merge hardcoded + already-cached dynamic providers for inspection/tooling
            var merged = new Dictionary<string, IAiProvider>(_hardcoded.Providers, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in _dynamic)
                merged[key] = value;
            return merged;
        }
    }

    /// <inheritdoc />
    public IAiProvider? GetProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        // Fast path: hardcoded (OpenAI, Anthropic, Google, Moonshot, …)
        var hardcoded = _hardcoded.GetProvider(providerName);
        if (hardcoded is not null)
            return hardcoded;

        // Second fast path: previously resolved dynamic provider (cache hit)
        if (_dynamic.TryGetValue(providerName, out var cached))
            return cached;

        // Slow path: query DB, build provider, cache it
        var loaded = LoadFromDatabase(providerName);
        if (loaded is not null)
            _dynamic[providerName] = loaded;

        return loaded;
    }

    /// <inheritdoc />
    public bool IsImplemented(string providerName)
        => GetProvider(providerName) is not null;

    /// <inheritdoc />
    public IEnumerable<string> GetImplementedProviderNames()
        => Providers.Keys.ToList();

    private IAiProvider? LoadFromDatabase(string providerName)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var entity = db.AiProviders
                .Where(p => p.Name == providerName && p.IsActive && p.BaseUrl != null)
                .Include(p => p.Models.Where(m => m.IsActive))
                .FirstOrDefault();

            if (entity is null)
                return null;

            var defaultModel = entity.Models
                .OrderByDescending(m => m.IsDefault)
                .ThenBy(m => m.SortOrder)
                .Select(m => m.ModelId)
                .FirstOrDefault();

            if (defaultModel is null)
            {
                _logger.LogWarning(
                    "Found provider '{Provider}' in database but it has no active models — skipping.",
                    providerName);
                return null;
            }

            var provider = new OpenAiCompatibleProvider(
                providerName: entity.Name,
                baseUrl: entity.BaseUrl!,
                defaultModel: defaultModel,
                logger: _loggerFactory.CreateLogger($"{entity.Name}Provider"));

            _logger.LogInformation(
                "Dynamically loaded OpenAI-compatible provider '{Provider}' → {BaseUrl} (default model: {Model}).",
                entity.Name, entity.BaseUrl, defaultModel);

            return provider;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load provider '{Provider}' from database.", providerName);
            return null;
        }
    }
}
