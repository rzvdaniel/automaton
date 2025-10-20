using Automaton.Core.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Background service that loads runner script engine settings at startup
/// Runs after DI container is fully initialized to avoid deadlocks
/// Also provides a method to reload settings when user updates them
/// </summary>
public class ScriptEngineSettingsInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ScriptEngineConfiguration _config;
    private readonly ILogger _logger;

    public ScriptEngineSettingsInitializer(
        IServiceProvider serviceProvider,
        ScriptEngineConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = Log.ForContext<ScriptEngineSettingsInitializer>();
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
            _logger.Information("Loading runner script engine settings...");

            using var scope = _serviceProvider.CreateScope();
            var settingsService = scope.ServiceProvider.GetService<RunnerSettingsService>();

            if (settingsService != null)
            {
                // Clear cache to force fresh load from database
                settingsService.ClearCache();
                
                var settings = await settingsService.LoadSettingsAsync();
                
                if (settings != null)
                {
                    _config.LoadFromRunnerSettings(settings);
                    _logger.Information("Successfully loaded runner script engine settings");
                    
                    if (_config.EnableDebugMode)
                    {
                        _logger.Debug("Python DLL: {PythonDll}", _config.PythonDllPath ?? "auto-detect");
                        _logger.Debug("Python Paths: {Count} configured", _config.PythonPaths?.Count ?? 0);
                    }
                }
                else
                {
                    _logger.Information("No custom runner settings found - using defaults");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load runner script engine settings - using defaults");
        }
    }
}
