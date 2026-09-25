
using Automaton.Mcp.SystemPrompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using Automaton.Api.Models;
using Automaton.Api.Data;
using Automaton.Api.Entities;

namespace Automaton.Api.Services;

/// <summary>
/// Service for managing user-specific settings with in-memory caching
/// </summary>
public class UserSettingsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserSettingsService> _logger;

    // Cache settings
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    // Setting keys
    public static class Keys
    {
        public const string EditorFontSize = "editor.fontSize";
        public const string AppearanceTheme = "appearance.theme";
        public const string DefaultAiModel = "ai.defaultModel";
        public const string DefaultAiProvider = "ai.defaultProvider";
        public const string DefaultPlanningModel = "ai.defaultPlanningModel";
        public const string DefaultPlanningProvider = "ai.defaultPlanningProvider";
        public const string ShowAiDebugDetails = "ai.showDebugDetails";
        public const string AppVerificationStrategy = "ai.appVerificationStrategy";
        public const string AppExecutionPolicy = "ai.appExecutionPolicy";
    }

    // Default values
    public static class Defaults
    {
        public const int EditorFontSize = 14;
        public const string Theme = "system";
    }

    public UserSettingsService(
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        ILogger<UserSettingsService> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get a setting value for a user, with caching
    /// </summary>
    public async Task<T> GetSettingAsync<T>(Guid userId, string key, T defaultValue)
    {
        var cacheKey = GetCacheKey(userId, key);

        if (_cache.TryGetValue(cacheKey, out T? cachedValue))
        {
            return cachedValue!;
        }

        try
        {
            var setting = await _dbContext.UserSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId && s.SettingKey == key);

            if (setting == null)
            {
                _cache.Set(cacheKey, defaultValue, CacheDuration);
                return defaultValue;
            }

            var value = JsonSerializer.Deserialize<T>(setting.SettingValue);
            if (value == null)
            {
                _cache.Set(cacheKey, defaultValue, CacheDuration);
                return defaultValue;
            }

            _cache.Set(cacheKey, value, CacheDuration);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving setting {Key} for user {UserId}", key, userId);
            return defaultValue;
        }
    }

    /// <summary>
    /// Set a setting value for a user
    /// </summary>
    public async Task SetSettingAsync<T>(Guid userId, string key, T value)
    {
        try
        {
            var setting = await _dbContext.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == userId && s.SettingKey == key);

            var jsonValue = JsonSerializer.Serialize(value);

            if (setting == null)
            {
                setting = new UserSetting
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SettingKey = key,
                    SettingValue = jsonValue,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.UserSettings.Add(setting);
            }
            else
            {
                setting.SettingValue = jsonValue;
                setting.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();

            // Update cache
            var cacheKey = GetCacheKey(userId, key);
            _cache.Set(cacheKey, value, CacheDuration);

            _logger.LogDebug("Saved setting {Key} for user {UserId}", key, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving setting {Key} for user {UserId}", key, userId);
            throw;
        }
    }

    /// <summary>
    /// Get editor settings for a user (convenience method)
    /// </summary>
    public async Task<EditorSettings> GetEditorSettingsAsync(Guid userId)
    {
        var fontSize = await GetSettingAsync(
            userId,
            Keys.EditorFontSize,
            Defaults.EditorFontSize);

        return new EditorSettings
        {
            FontSize = fontSize
        };
    }

    /// <summary>
    /// Update editor settings for a user (convenience method)
    /// </summary>
    public async Task UpdateEditorSettingsAsync(Guid userId, UpdateEditorSettingsRequest request)
    {
        await SetSettingAsync(userId, Keys.EditorFontSize, request.FontSize);
    }

    /// <summary>
    /// Get appearance settings for a user (convenience method)
    /// </summary>
    public async Task<AppearanceSettings> GetAppearanceSettingsAsync(Guid userId)
    {
        var theme = await GetSettingAsync(
            userId,
            Keys.AppearanceTheme,
            Defaults.Theme);

        return new AppearanceSettings
        {
            Theme = theme
        };
    }

    /// <summary>
    /// Update appearance settings for a user (convenience method)
    /// </summary>
    public async Task UpdateAppearanceSettingsAsync(Guid userId, UpdateAppearanceSettingsRequest request)
    {
        await SetSettingAsync(userId, Keys.AppearanceTheme, request.Theme);
    }

    /// <summary>
    /// Get all user settings (unified endpoint)
    /// </summary>
    public async Task<UserSettingsDto> GetAllSettingsAsync(Guid userId)
    {
        var theme = await GetSettingAsync(userId, Keys.AppearanceTheme, Defaults.Theme);
        var fontSize = await GetSettingAsync(userId, Keys.EditorFontSize, Defaults.EditorFontSize);
        var defaultAiModel = await GetSettingAsync<string?>(userId, Keys.DefaultAiModel, null);
        var defaultAiProvider = await GetSettingAsync<string?>(userId, Keys.DefaultAiProvider, null);
        var defaultPlanningModel = await GetSettingAsync<string?>(userId, Keys.DefaultPlanningModel, null);
        var defaultPlanningProvider = await GetSettingAsync<string?>(userId, Keys.DefaultPlanningProvider, null);
        var showAiDebugDetails = await GetSettingAsync(userId, Keys.ShowAiDebugDetails, false);
        var appVerificationStrategy = await GetSettingAsync<string?>(userId, Keys.AppVerificationStrategy, null);
        var appExecutionPolicy = await GetSettingAsync<string?>(userId, Keys.AppExecutionPolicy, null);

        return new UserSettingsDto
        {
            Theme = theme,
            FontSize = fontSize,
            DefaultAiModel = defaultAiModel,
            DefaultAiProvider = defaultAiProvider,
            DefaultPlanningModel = defaultPlanningModel,
            DefaultPlanningProvider = defaultPlanningProvider,
            ShowAiDebugDetails = showAiDebugDetails,
            AppVerificationStrategy = NormalizeVerificationStrategy(appVerificationStrategy),
            AppExecutionPolicy = NormalizeExecutionPolicy(appExecutionPolicy)
        };
    }

    /// <summary>
    /// Update user settings (partial update - only provided fields are updated)
    /// </summary>
    public async Task<UserSettingsDto> UpdateSettingsAsync(Guid userId, UpdateUserSettingsRequest request)
    {
        await UpdateAppearanceSettingsAsync(userId, request);
        await UpdateAiDefaultsAsync(userId, request);
        await UpdateAiPreferencesAsync(userId, request);

        return await GetAllSettingsAsync(userId);
    }

    private async Task UpdateAppearanceSettingsAsync(Guid userId, UpdateUserSettingsRequest request)
    {
        if (request.Theme != null)
            await SetSettingAsync(userId, Keys.AppearanceTheme, request.Theme);

        if (request.FontSize.HasValue)
            await SetSettingAsync(userId, Keys.EditorFontSize, request.FontSize.Value);
    }

    private async Task UpdateAiDefaultsAsync(Guid userId, UpdateUserSettingsRequest request)
    {
        if (request.HasDefaultAiModel)
            await SetSettingAsync<string?>(userId, Keys.DefaultAiModel, NormalizeOptionalText(request.DefaultAiModel));

        if (request.HasDefaultAiProvider)
            await SetSettingAsync<string?>(userId, Keys.DefaultAiProvider, NormalizeOptionalText(request.DefaultAiProvider));

        if (request.HasDefaultPlanningModel)
            await SetSettingAsync<string?>(userId, Keys.DefaultPlanningModel, NormalizeOptionalText(request.DefaultPlanningModel));

        if (request.HasDefaultPlanningProvider)
            await SetSettingAsync<string?>(userId, Keys.DefaultPlanningProvider, NormalizeOptionalText(request.DefaultPlanningProvider));
    }

    private async Task UpdateAiPreferencesAsync(Guid userId, UpdateUserSettingsRequest request)
    {
        if (request.HasShowAiDebugDetails && request.ShowAiDebugDetails.HasValue)
            await SetSettingAsync(userId, Keys.ShowAiDebugDetails, request.ShowAiDebugDetails.Value);

        if (request.HasAppVerificationStrategy)
            await SetSettingAsync<string?>(userId, Keys.AppVerificationStrategy,
                NormalizeVerificationStrategy(request.AppVerificationStrategy));

        if (request.HasAppExecutionPolicy)
            await SetSettingAsync<string?>(userId, Keys.AppExecutionPolicy,
                NormalizeExecutionPolicy(request.AppExecutionPolicy));
    }

    private static string? NormalizeOptionalText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Invalidate cached settings for a user
    /// </summary>
    public void InvalidateCache(Guid userId)
    {
        _cache.Remove(GetCacheKey(userId, Keys.EditorFontSize));
        _cache.Remove(GetCacheKey(userId, Keys.AppearanceTheme));
        _cache.Remove(GetCacheKey(userId, Keys.DefaultAiModel));
        _cache.Remove(GetCacheKey(userId, Keys.DefaultAiProvider));
        _cache.Remove(GetCacheKey(userId, Keys.DefaultPlanningModel));
        _cache.Remove(GetCacheKey(userId, Keys.DefaultPlanningProvider));
        _cache.Remove(GetCacheKey(userId, Keys.ShowAiDebugDetails));
        _cache.Remove(GetCacheKey(userId, Keys.AppVerificationStrategy));
        _cache.Remove(GetCacheKey(userId, Keys.AppExecutionPolicy));
    }

    private static string? NormalizeVerificationStrategy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Equals(AppVerificationStrategyNames.Continuous, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.Continuous;

        if (value.Equals(AppVerificationStrategyNames.FinalGate, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.FinalGate;

        if (value.Equals(AppVerificationStrategyNames.Balanced, StringComparison.OrdinalIgnoreCase))
            return AppVerificationStrategyNames.Balanced;

        return null;
    }

    private static string? NormalizeExecutionPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Equals(AppExecutionPolicyNames.LeanTools, StringComparison.OrdinalIgnoreCase))
            return AppExecutionPolicyNames.LeanTools;

        if (value.Equals(AppExecutionPolicyNames.Default, StringComparison.OrdinalIgnoreCase))
            return AppExecutionPolicyNames.Default;

        return null;
    }

    private static string GetCacheKey(Guid userId, string key) => $"user_settings:{userId}:{key}";
}
