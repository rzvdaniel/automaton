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
            // Configuration Priority Order:
            // 1. Cached value - Highest priority for current session changes
            // 2. User's stored preference - User's explicit choice should override defaults
            // 3. AppConfig defaults - Fallback values from appsettings.json/command line
            // 4. Exception if none available
            //
            // Rationale: User preferences should take priority over application defaults.
            // The appsettings.json should provide fallback defaults, not override user choices.
            
            if (!string.IsNullOrEmpty(cachedServerUrl))
            {
                return cachedServerUrl;
            }
            
            var storedServerUrl = storageService.LoadServerUrlAsync().Result;
            if (!string.IsNullOrEmpty(storedServerUrl))
            {
                cachedServerUrl = storedServerUrl;
                return storedServerUrl;
            }

            if (!string.IsNullOrEmpty(appConfig.BaseUrl))
            {
                return appConfig.BaseUrl;
            }

            throw new InvalidOperationException(
                "Server URL is not configured. Please run 'automaton-runner auth --server <url>' or set it during interactive setup.");
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
            
        var storedServerUrl = storageService.LoadServerUrlAsync().Result;
        if (!string.IsNullOrEmpty(storedServerUrl))
            return true;
            
        if (!string.IsNullOrEmpty(appConfig.BaseUrl))
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

    // Python configuration methods
    public Models.RunnerPythonConfig GetPythonConfig()
    {
        // Configuration Priority Order:
        // 1. Local runner.json file
        // 2. Environment variables
        // 3. Default (auto-detection enabled)
        
        var config = storageService.GetPythonConfig();
        
        // Override with environment variables if present
        var envPythonDll = Environment.GetEnvironmentVariable("AUTOMATON_PYTHON_DLL");
        if (!string.IsNullOrEmpty(envPythonDll))
        {
            config.PythonDllPath = envPythonDll;
        }
        
        var envPythonPaths = Environment.GetEnvironmentVariable("AUTOMATON_PYTHON_PATHS");
        if (!string.IsNullOrEmpty(envPythonPaths))
        {
            var separator = Path.PathSeparator;
            config.PythonPaths = envPythonPaths.Split(separator, StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        
        var envAutoDetect = Environment.GetEnvironmentVariable("AUTOMATON_PYTHON_AUTO_DETECT");
        if (!string.IsNullOrEmpty(envAutoDetect) && bool.TryParse(envAutoDetect, out var autoDetect))
        {
            config.AutoDetectPythonPaths = autoDetect;
        }
        
        return config;
    }
}

public class ClientAuthConfig
{
    public string LoginUserUrl { get; set; } = "api/account/loginuser";
    public string RefreshAccessTokenUrl { get; set; } = "api/token/refreshaccesstoken";
    public int RefreshTokenExpirationMinutesCheck { get; set; } = 2;
}