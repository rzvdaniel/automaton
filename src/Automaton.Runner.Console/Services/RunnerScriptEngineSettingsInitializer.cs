using Automaton.Core.Scripting.Engines;
using Automaton.Core.Scripting.Engines.Python;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Net.Http.Json;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Background service that loads runner script engine settings at startup
/// Runs after DI container is fully initialized to avoid deadlocks
/// Also provides a method to reload settings when user updates them
/// </summary>
public class RunnerScriptEngineSettingsInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ScriptEngineConfiguration _config;
    private readonly ILogger _logger;

    public RunnerScriptEngineSettingsInitializer(
        IServiceProvider serviceProvider,
        ScriptEngineConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = Log.ForContext<RunnerScriptEngineSettingsInitializer>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadSettingsAsync();
        await AutoDetectAndSavePythonSettingsAsync(cancellationToken);
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

    /// <summary>
    /// Autodetect Python environment and save to server
    /// </summary>
    private async Task AutoDetectAndSavePythonSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.Information("Autodetecting Python environment for runner...");
            var detector = new PythonEnvironmentDetector();
            var detected = detector.Detect();

            var pythonSettings = new PythonEngineSettings
            {
                AutoDetectedPythonPaths = new List<string> { detected.PythonHome ?? string.Empty },
                PythonVersion = detected.PythonVersion,
                PythonExecutablePath = detected.PythonExe,
                PythonDllPath = detected.PythonDllPath,
                EnvironmentVariables = detected.EnvironmentVariables,
                IsAutoDetectionSuccessful = detected.IsAutoDetectionSuccessful,
                DetectedDistributions = detected.Distributions,
                DetectedArchitecture = detected.Architecture,
                DetectedSitePackagesPaths = detected.SitePackagesPaths,
                Notes = detected.Notes
            };

            var runnerSettings = new RunnerScriptEngineSettings
            {
                Python = pythonSettings
            };

            using var scope = _serviceProvider.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<ConfigurationService>();
            var httpClient = scope.ServiceProvider.GetRequiredService<HttpClient>();
            var runnerId = configService.RunnerId;
            var runnersUrl = configService.RunnersUrl;
            var baseUrl = configService.BaseUrl;

            if (!string.IsNullOrEmpty(runnerId))
            {
                var url = $"{baseUrl}/{runnersUrl}/{runnerId}/script-settings";
                var json = JsonSerializer.Serialize(runnerSettings);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PutAsync(url, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.Information("Python environment autodetection saved to server for runner {RunnerId}", runnerId);
                }
                else
                {
                    _logger.Warning("Failed to save autodetected Python settings: {StatusCode}", response.StatusCode);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to autodetect or save Python environment settings");
        }
    }
}
