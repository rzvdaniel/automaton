using System.Text.Json;
using Automaton.Core.Scripting;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services;

/// <summary>
/// Service to manage desktop-specific script engine settings
/// Settings are stored in the database Settings table with key: "Desktop.PythonSettings"
/// </summary>
public class DesktopSettingsService
{
    private const string PythonSettingsKey = "Desktop.PythonSettings";
    
    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _configService;
    private readonly ILogger<DesktopSettingsService> _logger;
    private PythonEngineSettings? _cachedSettings;
    private DateTime _lastLoadTime = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);

    public DesktopSettingsService(
        HttpClient httpClient,
        ConfigurationService configService,
        ILogger<DesktopSettingsService> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Get Python engine settings for the desktop environment
    /// Uses caching to avoid excessive API calls
    /// </summary>
    public async Task<PythonEngineSettings?> GetPythonSettingsAsync()
    {
        try
        {
            // Return cached settings if still valid
            if (_cachedSettings != null && 
                DateTime.UtcNow - _lastLoadTime < _cacheLifetime)
            {
                _logger.LogDebug("Returning cached desktop Python settings");
                return _cachedSettings;
            }

            var url = $"{_configService.BaseUrl}/api/settings/python";
            _logger.LogDebug("Loading desktop Python settings from {Url}", url);

            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("No Python settings configured for desktop");
                    _cachedSettings = null;
                    _lastLoadTime = DateTime.UtcNow;
                    return null;
                }

                _logger.LogWarning("Failed to load desktop Python settings: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogInformation("No Python settings configured for desktop");
                _cachedSettings = null;
                _lastLoadTime = DateTime.UtcNow;
                return null;
            }

            var settings = JsonSerializer.Deserialize<PythonEngineSettings>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            _cachedSettings = settings;
            _lastLoadTime = DateTime.UtcNow;

            if (settings != null)
            {
                _logger.LogInformation("Loaded desktop Python settings");
                
                if (!string.IsNullOrWhiteSpace(settings.PythonDllPath))
                {
                    _logger.LogInformation("Python DLL path configured: {Path}", settings.PythonDllPath);
                }
                
                if (settings.PythonPaths?.Any() == true)
                {
                    _logger.LogInformation("Python paths configured: {Count} paths", settings.PythonPaths.Count);
                }
            }

            return settings;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error loading desktop Python settings");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize desktop Python settings");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading desktop Python settings");
            return null;
        }
    }

    /// <summary>
    /// Save Python engine settings for the desktop environment
    /// </summary>
    public async Task<bool> SavePythonSettingsAsync(PythonEngineSettings settings)
    {
        try
        {
            var url = $"{_configService.BaseUrl}/api/settings/python";
            _logger.LogDebug("Saving desktop Python settings to {Url}", url);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Saved desktop Python settings successfully");
                
                // Clear cache to force reload next time
                ClearCache();
                
                return true;
            }

            _logger.LogWarning("Failed to save desktop Python settings: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving desktop Python settings");
            return false;
        }
    }

    /// <summary>
    /// Clear cached settings to force reload on next request
    /// </summary>
    public void ClearCache()
    {
        _cachedSettings = null;
        _lastLoadTime = DateTime.MinValue;
        _logger.LogDebug("Cleared cached desktop Python settings");
    }
}
