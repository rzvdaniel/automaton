using System.Net.Http.Json;
using System.Text.Json;
using Automaton.Core.Scripting.Engines;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Service to load and manage runner-specific script engine settings
/// </summary>
public class RunnerSettingsService
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _configService;
    private readonly ILogger _logger;
    private RunnerScriptEngineSettings? _cachedSettings;
    private DateTime _lastLoadTime = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);

    public RunnerSettingsService(
        HttpClient httpClient,
        ConfigurationService configService)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = Log.ForContext<RunnerSettingsService>();
    }

    /// <summary>
    /// Load script engine settings for the current runner
    /// Uses caching to avoid excessive API calls
    /// </summary>
    /// <returns>Deserialized settings or null if not configured</returns>
    public async Task<RunnerScriptEngineSettings?> LoadSettingsAsync()
    {
        try
        {
            // Return cached settings if still valid
            if (_cachedSettings != null && 
                DateTime.UtcNow - _lastLoadTime < _cacheLifetime)
            {
                _logger.Debug("Returning cached runner script engine settings");
                return _cachedSettings;
            }

            var runnerId = _configService.RunnerId;
            
            if (string.IsNullOrEmpty(runnerId))
            {
                _logger.Warning("No runner ID configured - cannot load script engine settings");
                return null;
            }

            var url = $"{_configService.BaseUrl}{_configService.RunnersUrl}/{runnerId}/script-settings";
            _logger.Debug("Loading script engine settings from {Url}", url);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.Information("No script engine settings configured for runner {RunnerId}", runnerId);
                    _cachedSettings = null;
                    _lastLoadTime = DateTime.UtcNow;
                    return null;
                }

                _logger.Warning("Failed to load script engine settings: {StatusCode}", response.StatusCode);
                return null;
            }

            var settings = await response.Content.ReadFromJsonAsync<RunnerScriptEngineSettings>();
            
            _cachedSettings = settings;
            _lastLoadTime = DateTime.UtcNow;

            if (settings?.Python != null)
            {
                _logger.Information("Loaded script engine settings for runner {RunnerId}", runnerId);
                                
                if (settings.Python.PythonPaths?.Any() == true)
                {
                    _logger.Information("Python paths configured: {Count} paths", settings.Python.PythonPaths.Count);
                }
            }

            return settings;
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "HTTP error loading script engine settings for runner {RunnerId}", _configService.RunnerId);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.Error(ex, "Failed to deserialize script engine settings for runner {RunnerId}", _configService.RunnerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading script engine settings for runner {RunnerId}", _configService.RunnerId);
            return null;
        }
    }

    /// <summary>
    /// Clear cached settings to force reload on next request
    /// </summary>
    public void ClearCache()
    {
        _cachedSettings = null;
        _lastLoadTime = DateTime.MinValue;
        _logger.Debug("Cleared cached runner script engine settings");
    }
}
