using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service to manage application settings stored in database
/// </summary>
public class SettingsService
{
    private const string AppDeployRulesKey = "Apps.DeployRules";
    
    private readonly ApplicationDbContext _dbContext;
    private readonly UserContextService _userContextService;
    private readonly Serilog.ILogger _logger;

    public SettingsService(
        ApplicationDbContext dbContext,
        UserContextService userContextService)
    {
        _dbContext = dbContext;
        _userContextService = userContextService;
        _logger = Serilog.Log.ForContext<SettingsService>();
    }

    public async Task<AppDeployRulesSettings> GetEffectiveAppDeployRulesAsync()
    {
        var persisted = await GetSettingAsync<AppDeployRulesSettings>(AppDeployRulesKey);

        return new AppDeployRulesSettings
        {
            SourceExclude = MergeRules(
                [
                    ".env",
                    ".env.*",
                    ".DS_Store",
                    "*.pyc",
                    "*.log",
                    ".git",
                    ".automaton",
                    "node_modules",
                    "venv",
                    ".venv",
                    "__pycache__",
                    ".vite",
                    "coverage",
                    "dist"
                ],
                persisted?.SourceExclude),
            FrontendExclude = MergeRules(
                [
                    ".env",
                    ".env.*",
                    ".DS_Store",
                    "*.pyc",
                    "*.log",
                    ".git",
                    "node_modules",
                    "venv",
                    ".venv",
                    "__pycache__"
                ],
                persisted?.FrontendExclude),
            BackendExclude = MergeRules(
                [
                    ".env",
                    ".env.*",
                    ".DS_Store",
                    "*.pyc",
                    "*.log",
                    ".git",
                    "node_modules",
                    "venv",
                    ".venv",
                    "__pycache__"
                ],
                persisted?.BackendExclude)
        };
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
