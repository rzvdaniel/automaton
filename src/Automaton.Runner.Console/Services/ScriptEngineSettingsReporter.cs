using Automaton.Core.Scripting.Engines;
using Automaton.Core.Scripting.Engines.Python;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class ScriptEngineSettingsReporter
{
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly Serilog.ILogger _logger;

    public ScriptEngineSettingsReporter(HttpClient httpClient, IServiceProvider serviceProvider)
    {
        _httpClient = httpClient;
        _serviceProvider = serviceProvider;
        _logger = Serilog.Log.ForContext<ScriptEngineSettingsReporter>();
    }

    /// <summary>
    /// Submits Python environment settings to the server for this runner.
    /// </summary>
    public async Task SubmitPythonSettingsAsync(PythonEnvironmentInfo detected, CancellationToken cancellationToken)
    {
        try
        {
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
            var runnerId = configService.RunnerId;
            var runnersUrl = configService.RunnersUrl;
            var baseUrl = configService.BaseUrl;

            if (!string.IsNullOrEmpty(runnerId))
            {
                // Always use the latest BaseUrl from configService
                var url = $"{baseUrl}/{runnersUrl}/{runnerId}/script-settings";
                var json = System.Text.Json.JsonSerializer.Serialize(runnerSettings);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync(url, content, cancellationToken);
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
            _logger.Warning(ex, "Failed to submit Python environment settings to server");
        }
    }
}
