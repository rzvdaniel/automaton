using Automaton.Core.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Automaton.Studio.Services;

/// <summary>
/// Background service that loads desktop script engine settings at startup
/// Runs after DI container is fully initialized to avoid deadlocks
/// Also provides a method to reload settings when user updates them
/// </summary>
public class DesktopScriptEngineSettingsInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ScriptEngineConfiguration _config;
    private readonly ILogger _logger;

    public DesktopScriptEngineSettingsInitializer(
        IServiceProvider serviceProvider,
        ScriptEngineConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = Serilog.Log.ForContext<DesktopScriptEngineSettingsInitializer>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadSettingsAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reload settings from database
    /// Call this after user updates settings in the UI
    /// </summary>
    public async Task ReloadSettingsAsync()
    {
        try
        {
            _logger.Information("Loading desktop script engine settings...");

            using var scope = _serviceProvider.CreateScope();
            var settingsService = scope.ServiceProvider.GetService<DesktopSettingsService>();

            if (settingsService != null)
            {
                // Clear cache to force fresh load from database
                settingsService.ClearCache();
                
                var settings = await settingsService.GetPythonSettingsAsync();
                
                if (settings != null)
                {
                    _config.LoadFromDesktopSettings(settings);
                    _logger.Information("Successfully loaded desktop script engine settings");
                    
                    if (_config.EnableDebugMode)
                    {
                        _logger.Debug("Python DLL: {PythonDll}", _config.PythonDllPath ?? "auto-detect");
                        _logger.Debug("Python Paths: {Count} configured", _config.PythonPaths?.Count ?? 0);
                    }
                }
                else
                {
                    _logger.Information("No custom desktop settings found - using defaults");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load desktop script engine settings - using defaults");
        }
    }
}
