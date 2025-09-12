using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Automaton.Runner.Console.Services;

public class ConfigurationService
{
    private readonly AppConfig appConfig;
    private readonly ClientAuthConfig authConfig;
    private readonly FileStorageService storageService;

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
            if (string.IsNullOrEmpty(appConfig.BaseUrl))
            {
                throw new InvalidOperationException(
                    "Server URL is not configured. Please run 'automaton-runner auth --server <url>' or set it during interactive setup.");
            }
            return appConfig.BaseUrl;
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

    public bool IsServerConfigured() => !string.IsNullOrEmpty(appConfig.BaseUrl);

    public void UpdateServerUrl(string serverUrl)
    {
        if (!string.IsNullOrEmpty(serverUrl))
        {
            appConfig.BaseUrl = serverUrl.TrimEnd('/');
        }
    }
}

public class ClientAuthConfig
{
    public string LoginUserUrl { get; set; } = "api/account/loginuser";
    public string RefreshAccessTokenUrl { get; set; } = "api/token/refreshaccesstoken";
    public int RefreshTokenExpirationMinutesCheck { get; set; } = 2;
}