using Serilog;
using System.Text;
using System.Text.Json;
using static System.Console;

namespace Automaton.Runner.Console.Services;

public class RunnerHostService
{
    private readonly ConfigurationService configService;
    private readonly FileStorageService storageService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly HubService hubService;
    private readonly HttpClient httpClient;
    private readonly ILogger logger;

    private CancellationTokenSource? cancellationTokenSource;

    public RunnerHostService(
        ConfigurationService configService,
        FileStorageService storageService,
        ApiKeyAuthenticationService apiKeyAuthService,
        HubService hubService,
        HttpClient httpClient)
    {
        this.configService = configService;
        this.storageService = storageService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.hubService = hubService;
        this.httpClient = httpClient;
        this.logger = Log.ForContext<RunnerHostService>();
    }

    public async Task StartAsync(string runnerName, string? serverUrl = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(serverUrl))
            {
                configService.UpdateServerUrl(serverUrl);
            }

            WriteLine("=== Automaton Runner ===");
            WriteLine($"Server: {configService.BaseUrl}");
            WriteLine();

            // Check API key authentication
            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                WriteLine("??  API Key authentication required...");
                WriteLine("Options:");
                WriteLine("1. Set API key: automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx");
                WriteLine("2. Set environment variable: export AUTOMATON_API_KEY=ak_runner_xxxxxxxxxxxx");
                WriteLine();
                WriteLine("?? Get your API key from Automaton Studio ? Settings ? API Keys");
                return;
            }

            // Set up HTTP client authentication
            await SetupHttpClientAuth();

            // Check if runner is registered
            if (!configService.IsRunnerRegistered())
            {
                WriteLine("?? Registering runner...");
                var registerSuccess = await RegisterRunnerAsync(runnerName);
                if (!registerSuccess)
                {
                    WriteLine("? Runner registration failed. Cannot start runner.");
                    return;
                }
            }

            // Connect to hub
            var connected = await hubService.ConnectAsync();
            if (!connected)
            {
                WriteLine("? Failed to connect to server. Cannot start runner.");
                return;
            }

            // Start the runner
            cancellationTokenSource = new CancellationTokenSource();
            
            WriteLine();
            WriteLine("?? Runner is now active and waiting for workflows...");
            WriteLine("Press 'q' to quit, 's' for status");
            WriteLine();

            // Handle console input
            _ = Task.Run(async () =>
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var key = ReadKey(true);
                    switch (key.KeyChar.ToString().ToLower())
                    {
                        case "q":
                            WriteLine("?? Shutting down runner...");
                            cancellationTokenSource.Cancel();
                            break;
                        case "s":
                            await ShowStatusAsync();
                            break;
                    }

                    await Task.Delay(100);
                }
            }, cancellationTokenSource.Token);

            // Wait for shutdown signal
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }

            await hubService.DisconnectAsync();
            WriteLine("? Runner stopped");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error in runner host service");
            WriteLine($"? Error: {ex.Message}");
        }
    }

    public async Task RegisterAsync(string runnerName, string? serverUrl = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(serverUrl))
            {
                configService.UpdateServerUrl(serverUrl);
            }

            WriteLine($"?? Attempting to register runner '{runnerName}'...");
            WriteLine($"?? Server: {configService.BaseUrl}");

            // Check API key authentication first
            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                WriteLine("? API Key authentication required for runner registration.");
                WriteLine();
                WriteLine("?? Available authentication options:");
                WriteLine("1. Run auth command first: automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx");
                WriteLine("2. Set environment variable: export AUTOMATON_API_KEY=ak_runner_xxxxxxxxxxxx");
                WriteLine();
                WriteLine("?? Get your API key from Automaton Studio ? Settings ? API Keys");
                return;
            }

            WriteLine($"? Using API key from: {(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY") != null ? "Environment Variable" : "Stored Credentials")}");

            await SetupHttpClientAuth();

            var success = await RegisterRunnerAsync(runnerName);
            if (success)
            {
                WriteLine($"? Runner '{runnerName}' registered successfully");
            }
            else
            {
                WriteLine($"? Failed to register runner '{runnerName}'");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error registering runner");
            WriteLine($"? Registration error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear all stored data (credentials + runner registration)
    /// </summary>
    public async Task<bool> ClearAllDataAsync()
    {
        try
        {
            await storageService.DeleteApiKeyAsync();
            await storageService.DeleteRunnerConfigAsync();
            WriteLine("? All stored data cleared successfully!");
            logger.Information("All stored data cleared");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error clearing all data");
            WriteLine($"? Error clearing all data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Cleanup stored API key and runner configuration
    /// </summary>
    public async Task<bool> CleanupAsync()
    {
        try
        {
            WriteLine("?? Cleaning up stored runner data...");
            WriteLine();

            var apiKeyCleared = false;
            var runnerConfigCleared = false;

            // Clear API key if it exists
            var hasApiKey = !string.IsNullOrEmpty(await storageService.LoadApiKeyAsync());
            if (hasApiKey)
            {
                await storageService.DeleteApiKeyAsync();
                WriteLine("? Stored API key removed");
                apiKeyCleared = true;
            }

            // Clear runner configuration if it exists
            if (storageService.IsRunnerRegistered())
            {
                await storageService.DeleteRunnerConfigAsync();
                WriteLine("? Runner configuration removed");
                runnerConfigCleared = true;
            }

            if (!apiKeyCleared && !runnerConfigCleared)
            {
                WriteLine("??  No stored data found to cleanup");
            }
            else
            {
                WriteLine();
                WriteLine("? Cleanup completed successfully!");
                
                if (apiKeyCleared)
                {
                    WriteLine("   - API key authentication cleared");
                }
                if (runnerConfigCleared)
                {
                    WriteLine("   - Runner registration cleared");
                }
                
                WriteLine();
                WriteLine("?? Note: Environment variable AUTOMATON_API_KEY (if set) is not affected");
            }

            logger.Information("Runner cleanup completed. API key cleared: {ApiKeyCleared}, Runner config cleared: {RunnerConfigCleared}", apiKeyCleared, runnerConfigCleared);
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during cleanup");
            WriteLine($"? Error during cleanup: {ex.Message}");
            return false;
        }
    }

    private async Task SetupHttpClientAuth()
    {
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("No valid API key available for authentication. Please run 'auth' command first or set AUTOMATON_API_KEY environment variable.");
        }

        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        logger.Debug("HTTP client configured with API key authentication (X-API-Key header)");
    }

    private async Task<bool> RegisterRunnerAsync(string runnerName)
    {
        try
        {
            var runnerId = Guid.NewGuid();
            var runnerData = new
            {
                Id = runnerId,
                Name = runnerName
            };

            var json = JsonSerializer.Serialize(runnerData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            WriteLine($"?? Making registration request to: {configService.BaseUrl}/{configService.RunnersUrl}");
            WriteLine($"?? Runner data: ID={runnerId}, Name={runnerName}");
            WriteLine($"?? Request headers: X-API-Key = {(httpClient.DefaultRequestHeaders.Contains("X-API-Key") ? "Present" : "Missing")}");

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/{configService.RunnersUrl}", content);

            WriteLine($"?? Server response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                await storageService.SaveRunnerConfigAsync(runnerId.ToString(), runnerName);
                logger.Information("Runner {RunnerName} registered with ID {RunnerId}", runnerName, runnerId);
                WriteLine("? Runner registration successful!");
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            logger.Warning("Runner registration failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
            
            WriteLine($"? Registration failed: {response.StatusCode}");
            if (!string.IsNullOrEmpty(errorContent))
            {
                WriteLine($"?? Server response body: {errorContent}");
            }

            // Provide specific guidance based on status code
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                WriteLine();
                WriteLine("?? Authentication troubleshooting:");
                WriteLine("1. Verify the API key is valid (run 'auth' command with a valid key first)");
                WriteLine("2. Check if the API key has expired in Automaton Studio");
                WriteLine("3. Ensure the API key has sufficient scope (Runner, Execute, or FullAccess)");
                WriteLine("4. Verify the Automaton Studio server is running and accessible");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                WriteLine("The API key is valid but doesn't have permission for runner registration.");
                WriteLine("?? Ensure the API key has 'Runner', 'Execute', or 'FullAccess' scope (not 'ReadOnly').");
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during runner registration");
            WriteLine($"? Registration error: {ex.Message}");
            return false;
        }
    }

    private async Task ShowStatusAsync()
    {
        WriteLine();
        WriteLine("=== Runner Status ===");
        WriteLine($"Runner ID: {configService.RunnerId}");
        WriteLine($"Runner Name: {configService.ApplicationName}");
        WriteLine($"Server: {configService.BaseUrl}");
        WriteLine($"Connected: {hubService.IsConnected()}");
        
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        var hasEnvKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
        var hasStoredKey = !string.IsNullOrEmpty(await storageService.LoadApiKeyAsync());
        
        WriteLine($"Authenticated: {!string.IsNullOrEmpty(apiKey)}");
        if (!string.IsNullOrEmpty(apiKey))
        {
            WriteLine($"Auth Source: {(hasEnvKey ? "Environment Variable" : hasStoredKey ? "Stored API Key" : "Unknown")}");
        }
        WriteLine("==================");
        WriteLine();
    }
}