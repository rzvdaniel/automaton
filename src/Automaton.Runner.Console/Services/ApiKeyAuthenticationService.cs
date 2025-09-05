using Serilog;
using static System.Console;

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
                WriteLine("? Invalid API key format. Expected format: ak_xxxxxxxxxxxx");
                return false;
            }

            WriteLine($"?? Testing API key against server: {configService.BaseUrl}");
            WriteLine($"?? API key format: {apiKey.Substring(0, Math.Min(15, apiKey.Length))}...");
            WriteLine($"?? API key length: {apiKey.Length}");
            WriteLine($"?? Validation endpoint: {configService.BaseUrl}/api/apikeys/validate");

            // Use dedicated API key validation endpoint
            httpClient.DefaultRequestHeaders.Clear();
            // Don't add Content-Type to DefaultRequestHeaders - it belongs on the content!

            var requestBody = new { ApiKey = apiKey };
            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/api/apikeys/validate", content);
            
            WriteLine($"?? Server response: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                WriteLine($"?? Validation successful: {responseContent}");
                
                await storageService.SaveApiKeyAsync(apiKey);
                WriteLine("? API key validated and saved!");
                logger.Information("API key validated successfully");
                return true;
            }

            WriteLine($"? API key validation failed: {response.StatusCode}");
            
            // Get detailed error information
            var errorContent = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(errorContent))
            {
                WriteLine($"?? Server error details: {errorContent}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                WriteLine();
                WriteLine("?? Troubleshooting suggestions:");
                WriteLine("1. Verify the API key is correct and hasn't been modified");
                WriteLine("2. Check if the API key has expired in Automaton Studio");
                WriteLine("3. Ensure the API key has appropriate scope/permissions (not ReadOnly)");
                WriteLine("4. Verify the server URL is correct");
                WriteLine("5. Check if the Automaton Studio server is running and accessible");
                WriteLine("6. Verify the API key was created for the correct server/environment");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                WriteLine("The API key is valid but doesn't have permission to access runner operations.");
                WriteLine("?? Make sure the API key has 'Runner', 'Execute', or 'FullAccess' scope (not 'ReadOnly') in Automaton Studio.");
            }

            return false;
        }
        catch (HttpRequestException ex)
        {
            WriteLine($"?? Network error: {ex.Message}");
            WriteLine("?? Check if the Automaton Studio server is running and accessible at the specified URL.");
            logger.Error(ex, "Network error during API key validation");
            return false;
        }
        catch (Exception ex)
        {
            WriteLine($"? API key validation error: {ex.Message}");
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
                WriteLine("?? API key from environment variable has invalid format. Expected format: ak_xxxxxxxxxxxx");
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
            WriteLine("Stored API key format is invalid. Please set a new API key.");
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
        WriteLine("? API key removed successfully");
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