using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Api.Services;

/// <summary>
/// Provides the effective AiProviders config by merging DB-persisted operational settings
/// over the appsettings.json baseline. Results are cached in memory and invalidated
/// whenever SaveOperationalSettingsAsync is called — no restart required.
/// </summary>
public interface IAiProvidersSettingsService
{
    /// <summary>
    /// Returns the effective config: DB operational settings merged over appsettings baseline.
    /// Cached; invalidated on save.
    /// </summary>
    Task<AiProvidersConfig> GetEffectiveConfigAsync();

    /// <summary>Returns only the operational (editable) subset of the effective config.</summary>
    Task<AiProvidersOperationalConfig> GetOperationalSettingsAsync();

    /// <summary>Persists operational settings to the database and invalidates the cache.</summary>
    Task SaveOperationalSettingsAsync(AiProvidersOperationalConfig settings);
}

public class AiProvidersSettingsService : IAiProvidersSettingsService
{
    /// <summary>Key used in the Settings table.</summary>
    public const string SettingKey = "AiProviders";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiProvidersConfig    _baseline;
    private readonly SemaphoreSlim        _lock = new(1, 1);
    private AiProvidersConfig?            _cachedConfig;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public AiProvidersSettingsService(
        IServiceScopeFactory scopeFactory,
        IOptions<AiProvidersConfig> baseline)
    {
        _scopeFactory = scopeFactory;
        _baseline     = baseline.Value;
    }

    public async Task<AiProvidersConfig> GetEffectiveConfigAsync()
    {
        if (_cachedConfig != null) return _cachedConfig;

        await _lock.WaitAsync();
        try
        {
            if (_cachedConfig != null) return _cachedConfig;
            _cachedConfig = await LoadFromDbAsync();
            return _cachedConfig;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AiProvidersOperationalConfig> GetOperationalSettingsAsync()
    {
        var effective = await GetEffectiveConfigAsync();
        return ExtractOperational(effective);
    }

    public async Task SaveOperationalSettingsAsync(AiProvidersOperationalConfig settings)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var json     = JsonSerializer.Serialize(settings, JsonOptions);
        var existing = await db.Settings.FindAsync(SettingKey);

        if (existing == null)
        {
            db.Settings.Add(new Setting
            {
                Key         = SettingKey,
                Value       = json,
                Description = "AiProviders operational configuration (iteration limits, token limits, etc.)",
                UpdatedAt   = DateTime.UtcNow
            });
        }
        else
        {
            existing.Value     = json;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // Invalidate cache — next call to GetEffectiveConfigAsync reloads from DB
        await _lock.WaitAsync();
        try { _cachedConfig = null; }
        finally { _lock.Release(); }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task<AiProvidersConfig> LoadFromDbAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db      = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var setting = await db.Settings.FindAsync(SettingKey);

        if (setting == null) return _baseline;

        var operational = JsonSerializer.Deserialize<AiProvidersOperationalConfig>(setting.Value, JsonOptions);
        if (operational == null) return _baseline;

        return MergeIntoBaseline(operational);
    }

    /// <summary>
    /// Returns a new AiProvidersConfig with operational fields taken from <paramref name="op"/>
    /// and infrastructure config (Providers, AppMemory, etc.) carried over from the baseline.
    /// </summary>
    private AiProvidersConfig MergeIntoBaseline(AiProvidersOperationalConfig op) => new()
    {
        MaxConversationHistoryMessages = op.MaxConversationHistoryMessages,
        MaxIterations                  = op.MaxIterations,
        MaxConsecutiveFailures         = op.MaxConsecutiveFailures,
        DefaultMaxOutputTokens         = op.DefaultMaxOutputTokens,
        AppVerificationDefaultStrategy = op.AppVerificationDefaultStrategy,
        AppVerificationAllowUserOverride = op.AppVerificationAllowUserOverride,
        AppExecutionDefaultPolicy = op.AppExecutionDefaultPolicy,
        AppExecutionAllowUserOverride = op.AppExecutionAllowUserOverride,
        IterationLimits                = op.IterationLimits,
        HistoryCompaction              = op.HistoryCompaction,
        // Infrastructure config — stays from appsettings, not editable via admin UI
        Providers       = _baseline.Providers,
        AppMemory       = _baseline.AppMemory,
        AppInstructions = _baseline.AppInstructions,
        RateLimitRetry  = _baseline.RateLimitRetry,
        Streaming       = _baseline.Streaming
    };

    private static AiProvidersOperationalConfig ExtractOperational(AiProvidersConfig cfg) => new()
    {
        MaxConversationHistoryMessages = cfg.MaxConversationHistoryMessages,
        MaxIterations                  = cfg.MaxIterations,
        MaxConsecutiveFailures         = cfg.MaxConsecutiveFailures,
        DefaultMaxOutputTokens         = cfg.DefaultMaxOutputTokens,
        AppVerificationDefaultStrategy = cfg.AppVerificationDefaultStrategy,
        AppVerificationAllowUserOverride = cfg.AppVerificationAllowUserOverride,
        AppExecutionDefaultPolicy = cfg.AppExecutionDefaultPolicy,
        AppExecutionAllowUserOverride = cfg.AppExecutionAllowUserOverride,
        IterationLimits                = cfg.IterationLimits,
        HistoryCompaction              = cfg.HistoryCompaction
    };
}
