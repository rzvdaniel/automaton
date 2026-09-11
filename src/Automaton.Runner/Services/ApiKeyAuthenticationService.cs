using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

public class ApiKeyAuthenticationService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly FileStorageService storageService;
    private readonly ILogger logger;
    
    // Cache the API key in memory for the current session
    private string? _cachedApiKey;

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

    public async Task<bool> SetApiKeyAsync(string apiKey)
    {
        try
        {
            // Validate API key format
            if (!IsValidApiKeyFormat(apiKey))
            {
                WriteLine("❌ Invalid API key format. Expected format: ak_xxxxxxxxxxxx");
                return false;
            }

            WriteLine($"🔍 Testing API key against server: {configService.BaseUrl}");
            WriteLine($"🔍 API key format: {apiKey.Substring(0, Math.Min(15, apiKey.Length))}...");
            WriteLine($"🔍 API key length: {apiKey.Length}");
            WriteLine($"🔍 Validation endpoint: {configService.BaseUrl}/api/apikeys/validate");

            // Use dedicated API key validation endpoint
            httpClient.DefaultRequestHeaders.Clear();
            // Don't add Content-Type to DefaultRequestHeaders - it belongs on the content!

            var requestBody = new { ApiKey = apiKey };
            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/api/apikeys/validate", content);
            
            WriteLine($"🔍 Server response: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                WriteLine($"🔍 Validation successful: {responseContent}");
                
                // API key validated successfully - cache it in memory FIRST
                _cachedApiKey = apiKey;
                await storageService.SaveApiKeyAsync(apiKey);
                logger.Debug("API key cached in memory for current session");
                
                WriteLine();
                WriteLine("✅ API key validated successfully!");
                WriteLine();
                WriteLine("📝 Setting API key as persistent environment variable...");
                
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // Set for current user on Windows
                        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", apiKey, EnvironmentVariableTarget.User);
                        WriteLine("✅ API key saved as user environment variable (AUTOMATON_API_KEY)");
                        WriteLine("   This will persist across sessions and can be used by services.");
                        WriteLine();
                        WriteLine("⚠️  IMPORTANT: Restart your terminal for the environment variable to take effect!");
                        WriteLine("   Current session: API key is cached in memory (works for now)");
                        WriteLine("   Future sessions: Restart terminal to automatically load the API key");
                    }
                    else
                    {
                        // For Linux/macOS, we can't directly set persistent environment variables
                        // We need to guide the user to add it to their shell profile
                        WriteLine("⚠️  On Linux/macOS, environment variables must be added to your shell profile.");
                        WriteLine();
                        WriteLine("📝 Add this line to ~/.bashrc, ~/.zshrc, or your shell's config file:");
                        WriteLine($"   export AUTOMATON_API_KEY=\"{apiKey}\"");
                        WriteLine();
                        WriteLine("Then reload your shell:");
                        WriteLine("   source ~/.bashrc  # or source ~/.zshrc");
                        WriteLine();
                        WriteLine("💡 For now, the API key is set for this session only.");
                        
                        // Set for current process at least
                        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", apiKey, EnvironmentVariableTarget.Process);
                    }
                }
                catch (Exception ex)
                {
                    WriteLine($"⚠️  Could not set environment variable automatically: {ex.Message}");
                    WriteLine();
                    WriteLine("📝 Please set it manually:");
                    if (OperatingSystem.IsWindows())
                    {
                        WriteLine($"   [System.Environment]::SetEnvironmentVariable('AUTOMATON_API_KEY', '{apiKey}', 'User')");
                    }
                    else
                    {
                        WriteLine($"   export AUTOMATON_API_KEY=\"{apiKey}\"");
                        WriteLine($"   echo 'export AUTOMATON_API_KEY=\"{apiKey}\"' >> ~/.bashrc");
                    }
                    logger.Warning(ex, "Failed to set environment variable automatically");
                }
                
                WriteLine();
                logger.Debug("API key validated, cached, and persisted successfully");
                return true;
            }

            WriteLine($"❌ API key validation failed: {response.StatusCode}");
            
            // Get detailed error information
            var errorContent = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrEmpty(errorContent))
            {
                WriteLine($"🔍 Server error details: {errorContent}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                WriteLine();
                WriteLine("🔧 Troubleshooting suggestions:");
                WriteLine("1. Verify the API key is correct and hasn't been modified");
                WriteLine("2. Check if the API key has expired in Automaton Studio");
                WriteLine("3. Ensure the credential is a system-issued Runner credential");
                WriteLine("4. Verify the server URL is correct");
                WriteLine("5. Check if the Automaton Studio server is running and accessible");
                WriteLine("6. Verify the API key was created for the correct server/environment");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                WriteLine("The API key is valid but doesn't have permission to access runner operations.");
                WriteLine("🔧 Re-enroll or reprovision the Runner from Automaton Studio to obtain a system-issued Runner credential.");
            }

            return false;
        }
        catch (HttpRequestException ex)
        {
            WriteLine($"🔧 Network error: {ex.Message}");
            WriteLine("🔧 Check if the Automaton Studio server is running and accessible at the specified URL.");
            logger.Error(ex, "Network error during API key validation");
            return false;
        }
        catch (Exception ex)
        {
            WriteLine($"❌ API key validation error: {ex.Message}");
            logger.Error(ex, "API key validation error occurred");
            return false;
        }
    }

    public async Task<string?> GetValidApiKeyAsync()
    {
        // PRIORITY 1: Check in-memory cache (for current session after SetApiKeyAsync)
        if (!string.IsNullOrEmpty(_cachedApiKey))
        {
            if (IsValidApiKeyFormat(_cachedApiKey))
            {
                logger.Debug("Using cached API key from memory");
                return _cachedApiKey;
            }
        }
        
        // PRIORITY 2: Try to read from environment variable (persisted key or externally set)
        var apiKey = await storageService.LoadApiKeyAsync();
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            if (IsValidApiKeyFormat(apiKey))
            {
                logger.Debug("Using API key from configured Runner credential");
                // Cache it for future requests in this session
                _cachedApiKey = apiKey;
                return apiKey;
            }
            else
            {
                WriteLine("🔧 Configured API key has invalid format. Expected format: ak_xxxxxxxxxxxx");
                logger.Warning("Configured API key has invalid format");
            }
        }

        return null;
    }

    public async Task<bool> ValidateRunnerCredentialWithServerAsync(string apiKey)
    {
        if (!IsValidApiKeyFormat(apiKey))
        {
            return false;
        }

        try
        {
            httpClient.DefaultRequestHeaders.Clear();
            var json = System.Text.Json.JsonSerializer.Serialize(new { ApiKey = apiKey });
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync($"{configService.BaseUrl}/api/apikeys/validate", content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Runner credential validation request failed");
            return false;
        }
    }

    public Task SetEnrolledApiKeyAsync(string apiKey)
    {
        _cachedApiKey = apiKey;
        return storageService.SaveApiKeyAsync(apiKey);
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var apiKey = await GetValidApiKeyAsync();
        return !string.IsNullOrEmpty(apiKey);
    }

    public Task LogoutAsync()
    {
        // Clear in-memory cache
        _cachedApiKey = null;
        
        WriteLine("❌ To remove the API key, unset the environment variable:");
        WriteLine();
        
        if (OperatingSystem.IsWindows())
        {
            WriteLine("   For current session (PowerShell):");
            WriteLine("   Remove-Item Env:AUTOMATON_API_KEY");
            WriteLine();
            WriteLine("   For persistent removal:");
            WriteLine("   [System.Environment]::SetEnvironmentVariable('AUTOMATON_API_KEY', $null, 'User')");
        }
        else
        {
            WriteLine("   For current session:");
            WriteLine("   unset AUTOMATON_API_KEY");
            WriteLine();
            WriteLine("   For persistent removal (remove from ~/.bashrc or ~/.zshrc):");
            WriteLine("   sed -i '/AUTOMATON_API_KEY/d' ~/.bashrc");
        }
        
        WriteLine();
        logger.Debug("API key logout instructions displayed and memory cache cleared");
        return Task.CompletedTask;
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
