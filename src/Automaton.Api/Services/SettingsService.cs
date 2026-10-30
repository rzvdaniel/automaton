
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Automaton.Api.Models;
using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Entities;

namespace Automaton.Api.Services;

/// <summary>
/// Service to manage application settings stored in database
/// </summary>
public class SettingsService
{
    private const string AppDeployRulesKey = "Apps.DeployRules";
    
    private readonly ApplicationDbContext _dbContext;
    private readonly AppFileRuleBaselineOptions _appFileRuleBaseline;
    private readonly Serilog.ILogger _logger;

    public SettingsService(
        ApplicationDbContext dbContext,
        IOptions<AppFileRuleBaselineOptions> appFileRuleBaseline)
    {
        _dbContext = dbContext;
        _appFileRuleBaseline = appFileRuleBaseline.Value ?? new AppFileRuleBaselineOptions();
        _logger = Serilog.Log.ForContext<SettingsService>();
    }

    public async Task<AppDeployRulesSettings> GetEffectiveAppDeployRulesAsync()
    {
        var persisted = await GetAppDeployRulesAsync();

        return new AppDeployRulesSettings
        {
            SourceExclude = MergeRules(
                _appFileRuleBaseline.SourceExclude,
                persisted?.SourceExclude),
            FrontendExclude = MergeRules(
                _appFileRuleBaseline.FrontendExclude,
                persisted?.FrontendExclude),
            BackendExclude = MergeRules(
                _appFileRuleBaseline.BackendExclude,
                persisted?.BackendExclude),
            RemixSourceExclude = MergeRules(
                _appFileRuleBaseline.RemixSourceExclude,
                persisted?.RemixSourceExclude),
            RemixFrontendExclude = MergeRules(
                _appFileRuleBaseline.RemixFrontendExclude,
                persisted?.RemixFrontendExclude)
        };
    }

    public async Task<AppDeployRulesSettings> GetAppDeployRulesAsync()
    {
        return await GetSettingAsync<AppDeployRulesSettings>(AppDeployRulesKey) ?? new AppDeployRulesSettings();
    }

    public Task SaveAppDeployRulesAsync(AppDeployRulesSettings settings)
    {
        return SaveSettingAsync(
            AppDeployRulesKey,
            settings,
            "App deploy exclude defaults for source/frontend/backend packaging.");
    }

    private async Task<T?> GetSettingAsync<T>(string key)
    {
        try
        {
            var setting = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(
                setting.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to deserialize setting {SettingKey}", key);
            return default;
        }
    }

    private async Task SaveSettingAsync<T>(string key, T value, string description)
    {
        var json = JsonSerializer.Serialize(value);
        var setting = await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);

        if (setting == null)
        {
            setting = new Setting
            {
                Key = key,
                Value = json,
                Description = description,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Settings.Add(setting);
        }
        else
        {
            setting.Value = json;
            setting.Description = description;
            setting.UpdatedAt = DateTime.UtcNow;
            _dbContext.Entry(setting).State = EntityState.Modified;
        }

        await _dbContext.SaveChangesAsync();
    }

    private static List<string> MergeRules(IEnumerable<string> defaults, IEnumerable<string>? overrides)
    {
        return defaults
            .Concat(overrides ?? [])
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
