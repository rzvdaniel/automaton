using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

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
            // 2. Environment variable - Perfect for services and Docker (AppConfig__BaseUrl)
            // 3. User's stored preference - User's explicit choice from interactive setup
            // 4. Exception if none available
            //
            // Rationale: Environment variables are industry standard for service configuration.
            // They work reliably with Docker, Kubernetes, CI/CD, and Windows services.
            // File fallback provides convenience for interactive/standalone usage.
            
            if (!string.IsNullOrEmpty(cachedServerUrl))
            {
                return cachedServerUrl;
            }
            
            // Check environment variable first (works for services!)
            if (!string.IsNullOrEmpty(appConfig.BaseUrl))
            {
                cachedServerUrl = appConfig.BaseUrl;
                return appConfig.BaseUrl;
            }
            
            // Fall back to stored file (user preference from interactive setup)
            var storedServerUrl = storageService.LoadServerUrlAsync().Result;
            if (!string.IsNullOrEmpty(storedServerUrl))
            {
                cachedServerUrl = storedServerUrl;
                return storedServerUrl;
            }

            throw new InvalidOperationException(
                "Server URL is not configured. Please set AppConfig__BaseUrl environment variable or run 'automaton-runner server --url <url>' for interactive setup.");
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
        if (!string.IsNullOrEmpty(cachedServerUrl))
            return true;
        
        // Check environment variable first (12-factor app pattern)
        if (!string.IsNullOrEmpty(appConfig.BaseUrl))
            return true;
            
        // Check stored file (user preference)
        var storedServerUrl = storageService.LoadServerUrlAsync().Result;
        if (!string.IsNullOrEmpty(storedServerUrl))
            return true;
            
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
}

public class ClientAuthConfig
{
    public string LoginUserUrl { get; set; } = "api/account/loginuser";
    public string RefreshAccessTokenUrl { get; set; } = "api/token/refreshaccesstoken";
    public int RefreshTokenExpirationMinutesCheck { get; set; } = 2;
}