using Automaton.Runner.Console.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Serilog;

namespace Automaton.Runner.Console.Services;

public class ConfigurationService
{
    private readonly AppConfig appConfig;
    private readonly ClientAuthConfig authConfig;
    private readonly FileStorageService storageService;
    private string? cachedServerUrl;

    public ConfigurationService(IOptions<AppConfig> appConfigOptions, IConfiguration configuration, FileStorageService storageService)
    {
        this.appConfig = appConfigOptions.Value;
        this.storageService = storageService;
        
        // Bind the ClientAuthConfig section
        this.authConfig = new ClientAuthConfig();
        configuration.GetSection("ClientAuthConfig").Bind(authConfig);
    }

    public string RunnerId => storageService.GetRunnerId() ?? string.Empty;
    public string ApplicationName => storageService.GetRunnerName() ?? "Console-Runner";
    public string ApplicationType => appConfig.ApplicationType;

    public string BaseUrl 
    { 
        get 
        {
            // Configuration Priority Order (12-Factor App Pattern):
            // 1. Cached value - Highest priority for current session changes
            // 2. User's stored preference - User's explicit choice from interactive setup
            // 3. Environment variable - Perfect for services and Docker (AUTOMATON_API_URL)
            // 4. Exception if none available
            //
            // Rationale: Environment variables are industry standard for service configuration.
            // They work reliably with Docker, Kubernetes, CI/CD, and Windows services.
            // File fallback provides convenience for interactive/standalone usage.

            // DEBUG: Log configuration resolution process
            Log.Debug("BaseUrl resolution started");
            
            if (!string.IsNullOrEmpty(cachedServerUrl))
            {
                Log.Debug("Using cached server URL: {CachedUrl}", cachedServerUrl);
                return cachedServerUrl;
            }
            Log.Debug("No cached server URL found");
                        
            // Stored file (user preference from interactive setup)
            Log.Debug("Attempting to load server URL from file storage");
            var storedServerUrl = storageService.LoadServerUrlAsync().Result;
            if (!string.IsNullOrEmpty(storedServerUrl))
            {
                Log.Debug("Using stored file server URL: {StoredUrl}", storedServerUrl);
                cachedServerUrl = storedServerUrl;
                return storedServerUrl;
            }
            Log.Debug("No stored file server URL found");

            // Fall back to check environment variable (works for services!)
            if (!string.IsNullOrEmpty(appConfig.BaseUrl))
            {
                Log.Debug("Using environment variable server URL: {EnvUrl}", appConfig.BaseUrl);
                cachedServerUrl = appConfig.BaseUrl;
                return appConfig.BaseUrl;
            }
            Log.Debug("No environment variable server URL found (AppConfig.BaseUrl is empty)");

            Log.Warning("Server URL is not configured from any source (cached, environment, or file)");
            throw new InvalidOperationException(
                "Server URL is not configured. Please set AUTOMATON_API_URL environment variable or run 'automaton-runner server --url <url>' for interactive setup.");
        }
    }

    public string WorkflowHubUrl => appConfig.WorkflowHubUrl;
    public string FlowsUrl => appConfig.FlowsUrl;
    public string RunnersUrl => appConfig.RunnersUrl;
    public string LogsUrl => appConfig.LogsUrl;
    public string FlowExecutionUrl => appConfig.FlowExecutionUrl;

    // Authentication endpoints
    public string LoginUserUrl => authConfig.LoginUserUrl;
    public string RefreshAccessTokenUrl => authConfig.RefreshAccessTokenUrl;

    public bool IsRunnerRegistered() => storageService.IsRunnerRegistered();

    public bool IsServerConfigured() 
    {
        // Check all possible sources for server URL using the same priority order as BaseUrl
        Log.Debug("IsServerConfigured check started");
        
        if (!string.IsNullOrEmpty(cachedServerUrl))
        {
            Log.Debug("Server configured: cached value present");
            return true;
        }
        
        // Check environment variable first (12-factor app pattern)
        if (!string.IsNullOrEmpty(appConfig.BaseUrl))
        {
            Log.Debug("Server configured: environment variable present - {EnvUrl}", appConfig.BaseUrl);
            return true;
        }
        Log.Debug("Environment variable check: AppConfig.BaseUrl is empty");
            
        // Check stored file (user preference)
        var storedServerUrl = storageService.LoadServerUrlAsync().Result;
        if (!string.IsNullOrEmpty(storedServerUrl))
        {
            Log.Debug("Server configured: stored file present - {StoredUrl}", storedServerUrl);
            return true;
        }
        Log.Debug("Stored file check: no server URL found in file storage");
        
        Log.Warning("Server NOT configured: no source available (cached, environment, or file)");
        return false;
    }

    public async Task UpdateServerUrlAsync(string serverUrl)
    {
        if (!string.IsNullOrEmpty(serverUrl))
        {
            var cleanUrl = serverUrl.TrimEnd('/');
            
            // Update cached value immediately for current session
            cachedServerUrl = cleanUrl;
            
            // Update app config for this session (legacy compatibility)
            appConfig.BaseUrl = cleanUrl;
            
            // Persist to storage for future sessions (user preference)
            await storageService.SaveServerConfigAsync(cleanUrl);
        }
    }

    /// <summary>
    /// Clear the cached server URL. Used during cleanup to force reload from storage on next access.
    /// </summary>
    public void ClearCachedServerUrl()
    {
        Log.Information("Clearing cached server URL");
        cachedServerUrl = null;
    }
}
