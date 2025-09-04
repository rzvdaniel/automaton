using Serilog;

namespace Automaton.Runner.Console.Services;

public class ApiKeyAuthenticationService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly FileStorageService storageService;
    private readonly ILogger logger;

    public ApiKeyAuthenticationService(
        HttpClient httpClient, 
        ConfigurationService configService,
        FileStorageService storageService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.storageService = storageService;
        this.logger = Log.ForContext<ApiKeyAuthenticationService>();
    }

    public async Task<bool> SetApiKeyAsync(string apiKey, string? serverUrl = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(serverUrl))
            {
                configService.UpdateServerUrl(serverUrl);
            }

            // Validate API key format
            if (!IsValidApiKeyFormat(apiKey))
            {
                System.Console.WriteLine("?? Invalid API key format. Expected format: ak_runner_xxxxxxxxxxxx");
                return false;
            }

            // Test the API key by making a simple authenticated request
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await httpClient.GetAsync($"{configService.BaseUrl}/api/runners");
            
            if (response.IsSuccessStatusCode)
            {
                await storageService.SaveApiKeyAsync(apiKey);
                System.Console.WriteLine("? API key validated and saved!");
                logger.Information("API key validated successfully");
                return true;
            }

            System.Console.WriteLine($"? API key validation failed: {response.StatusCode}");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                System.Console.WriteLine("The API key is invalid or has expired.");
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"? API key validation error: {ex.Message}");
            logger.Error(ex, "API key validation error occurred");
            return false;
        }
    }

    public async Task<string?> GetValidApiKeyAsync()
    {
        // First check environment variable
        var envApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        if (!string.IsNullOrEmpty(envApiKey))
        {
            if (IsValidApiKeyFormat(envApiKey))
            {
                logger.Debug("Using API key from environment variable");
                return envApiKey;
            }
            else
            {
                System.Console.WriteLine("?? API key from environment variable has invalid format. Expected format: ak_runner_xxxxxxxxxxxx");
                logger.Warning("API key from environment variable has invalid format");
            }
        }

        // Fallback to stored API key
        var apiKey = await storageService.LoadApiKeyAsync();

        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        // Simple validation - in production you might want to test it with a lightweight API call
        if (!IsValidApiKeyFormat(apiKey))
        {
            System.Console.WriteLine("Stored API key format is invalid. Please set a new API key.");
            await storageService.DeleteApiKeyAsync();
            return null;
        }

        return apiKey;
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var apiKey = await GetValidApiKeyAsync();
        return !string.IsNullOrEmpty(apiKey);
    }

    public async Task LogoutAsync()
    {
        await storageService.DeleteApiKeyAsync();
        System.Console.WriteLine("? API key removed successfully");
        logger.Information("API key removed");
    }

    private static bool IsValidApiKeyFormat(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        // API keys should start with ak_ followed by scope (runner, execute, readonly) and then the key
        return apiKey.StartsWith("ak_runner_") || 
               apiKey.StartsWith("ak_execute_") || 
               apiKey.StartsWith("ak_readonly_");
    }
}