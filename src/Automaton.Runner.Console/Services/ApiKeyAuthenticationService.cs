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
                System.Console.WriteLine("? Invalid API key format. Expected format: ak_xxxxxxxxxxxx");
                return false;
            }

            System.Console.WriteLine($"?? Testing API key against server: {configService.BaseUrl}");
            System.Console.WriteLine($"?? API key format: {apiKey.Substring(0, Math.Min(15, apiKey.Length))}...");
            System.Console.WriteLine($"?? API key length: {apiKey.Length}");
            System.Console.WriteLine($"?? Validation endpoint: {configService.BaseUrl}/api/runners");

            // Try X-API-Key header first (primary method)
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var response = await httpClient.GetAsync($"{configService.BaseUrl}/api/runners");
            
            System.Console.WriteLine($"?? Server response (X-API-Key): {response.StatusCode}");
            
            // If X-API-Key fails with 401, try Authorization Bearer as fallback
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                System.Console.WriteLine("?? Trying Authorization Bearer header as fallback...");
                
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                
                response = await httpClient.GetAsync($"{configService.BaseUrl}/api/runners");
                System.Console.WriteLine($"?? Server response (Authorization Bearer): {response.StatusCode}");
            }
            
            if (response.IsSuccessStatusCode)
            {
                await storageService.SaveApiKeyAsync(apiKey);
                System.Console.WriteLine("? API key validated and saved!");
                logger.Information("API key validated successfully");
                return true;
            }

            System.Console.WriteLine($"? API key validation failed: {response.StatusCode}");
            
            // Get detailed error information
            var errorContent = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(errorContent))
            {
                System.Console.WriteLine($"?? Server error details: {errorContent}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                System.Console.WriteLine();
                System.Console.WriteLine("?? Troubleshooting suggestions:");
                System.Console.WriteLine("1. Verify the API key is correct and hasn't been modified");
                System.Console.WriteLine("2. Check if the API key has expired in Automaton Studio");
                System.Console.WriteLine("3. Ensure the API key has 'Runner' scope/permissions");
                System.Console.WriteLine("4. Verify the server URL is correct");
                System.Console.WriteLine("5. Check if the Automaton Studio server is running and accessible");
                System.Console.WriteLine("6. Verify the API key was created for the correct server/environment");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                System.Console.WriteLine("The API key is valid but doesn't have permission to access runner endpoints.");
                System.Console.WriteLine("?? Make sure the API key has 'Runner' scope in Automaton Studio.");
            }

            return false;
        }
        catch (HttpRequestException ex)
        {
            System.Console.WriteLine($"?? Network error: {ex.Message}");
            System.Console.WriteLine("?? Check if the Automaton Studio server is running and accessible at the specified URL.");
            logger.Error(ex, "Network error during API key validation");
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
                System.Console.WriteLine("?? API key from environment variable has invalid format. Expected format: ak_xxxxxxxxxxxx");
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

        // API keys should start with ak_ and be at least 10 characters long
        // This is more flexible to accommodate different key generation patterns
        return apiKey.StartsWith("ak_") && apiKey.Length >= 10;
    }
}