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

    /// <summary>
    /// Interactive start - guides user through setup if needed
    /// </summary>
    public async Task StartInteractiveAsync()
    {
        try
        {
            WriteLine("=== Automaton Runner - Interactive Setup ===");
            WriteLine();

            // Step 1: Check/Configure Server
            if (string.IsNullOrEmpty(configService.BaseUrl) || configService.BaseUrl.Contains("localhost"))
            {
                WriteLine("?? Server configuration needed...");
                WriteLine($"Current server: {configService.BaseUrl ?? "Not configured"}");
                WriteLine();
                
                Write("Enter server URL (or press Enter for default https://localhost:7091): ");
                var serverUrl = ReadLine();
                
                if (string.IsNullOrWhiteSpace(serverUrl))
                {
                    serverUrl = "https://localhost:7091";
                }
                
                configService.UpdateServerUrl(serverUrl);
                WriteLine($"? Server configured: {configService.BaseUrl}");
                WriteLine();
            }
            else
            {
                WriteLine($"? Server: {configService.BaseUrl}");
            }

            // Step 2: Check Authentication
            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                WriteLine("?? Authentication required...");
                WriteLine();
                WriteLine("You need an API key to authenticate with the Automaton server.");
                WriteLine("Get your API key from: Automaton Studio ? Settings ? API Keys");
                WriteLine();
                
                Write("Enter your API key (starts with 'ak_'): ");
                var inputApiKey = ReadLine();
                
                if (string.IsNullOrWhiteSpace(inputApiKey))
                {
                    WriteLine("? API key is required. Exiting.");
                    return;
                }
                
                await apiKeyAuthService.SetApiKeyAsync(inputApiKey, null);
                WriteLine("? Authentication configured successfully!");
                WriteLine();
            }
            else
            {
                var hasEnvKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
                WriteLine($"? Authenticated via: {(hasEnvKey ? "Environment Variable" : "Stored API Key")}");
            }

            // Set up HTTP client authentication
            await SetupHttpClientAuth();

            // Step 3: Check/Configure Runner Registration
            if (!configService.IsRunnerRegistered())
            {
                WriteLine("?? Runner registration required...");
                WriteLine();
                
                Write("Enter a name for this runner (e.g., 'My-Server-Runner'): ");
                var runnerName = ReadLine();
                
                if (string.IsNullOrWhiteSpace(runnerName))
                {
                    runnerName = $"AutoRunner-{Environment.MachineName}";
                    WriteLine($"Using default name: {runnerName}");
                }
                
                var registerSuccess = await RegisterRunnerAsync(runnerName);
                if (!registerSuccess)
                {
                    WriteLine("? Runner registration failed. Cannot start runner.");
                    return;
                }
                WriteLine();
            }
            else
            {
                WriteLine($"? Runner registered: {configService.ApplicationName} (ID: {configService.RunnerId})");
            }

            // Step 4: Start the runner
            await StartRunnerAsync();
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error in interactive runner setup. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Setup error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
        }
    }

    /// <summary>
    /// Interactive authentication
    /// </summary>
    public async Task AuthenticateInteractiveAsync(string? apiKey = null, string? serverUrl = null)
    {
        try
        {
            WriteLine("=== Automaton Runner - Authentication ===");
            WriteLine();

            // Configure server if provided or prompt
            if (!string.IsNullOrEmpty(serverUrl))
            {
                configService.UpdateServerUrl(serverUrl);
                WriteLine($"? Server configured: {configService.BaseUrl}");
            }
            else if (string.IsNullOrEmpty(configService.BaseUrl) || configService.BaseUrl.Contains("localhost"))
            {
                WriteLine($"Current server: {configService.BaseUrl ?? "Not configured"}");
                Write("Enter server URL (or press Enter for https://localhost:7091): ");
                var inputServerUrl = ReadLine();
                
                if (string.IsNullOrWhiteSpace(inputServerUrl))
                {
                    inputServerUrl = "https://localhost:7091";
                }
                
                configService.UpdateServerUrl(inputServerUrl);
                WriteLine($"? Server configured: {configService.BaseUrl}");
            }

            // Handle API key authentication
            if (!string.IsNullOrEmpty(apiKey))
            {
                await apiKeyAuthService.SetApiKeyAsync(apiKey, null);
                WriteLine("? Authentication successful!");
            }
            else
            {
                WriteLine("Get your API key from: Automaton Studio ? Settings ? API Keys");
                WriteLine();
                Write("Enter your API key (starts with 'ak_'): ");
                var inputApiKey = ReadLine();
                
                if (string.IsNullOrWhiteSpace(inputApiKey))
                {
                    WriteLine("? API key is required.");
                    return;
                }
                
                await apiKeyAuthService.SetApiKeyAsync(inputApiKey, null);
                WriteLine("? Authentication configured successfully!");
            }

            // Show next steps
            WriteLine();
            WriteLine("?? Next steps:");
            WriteLine("1. Register runner: automaton-runner register");
            WriteLine("2. Start runner: automaton-runner");
            WriteLine("   (or just run 'automaton-runner' - it will guide you through registration if needed)");
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error in interactive authentication. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Authentication error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
        }
    }

    /// <summary>
    /// Interactive registration
    /// </summary>
    public async Task RegisterInteractiveAsync(string? runnerName = null, bool force = false)
    {
        try
        {
            WriteLine("=== Automaton Runner - Registration ===");
            WriteLine($"Server: {configService.BaseUrl}");
            WriteLine();

            // Check if already registered and not forcing
            if (configService.IsRunnerRegistered() && !force)
            {
                WriteLine($"? Runner already registered: {configService.ApplicationName} (ID: {configService.RunnerId})");
                WriteLine();
                Write("Do you want to register with a different name? (y/N): ");
                var response = ReadLine()?.Trim().ToLower();
                
                if (response != "y" && response != "yes")
                {
                    WriteLine("Registration cancelled.");
                    return;
                }
                force = true;
            }

            // Check authentication
            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                WriteLine("? Authentication required before registration.");
                WriteLine("Run: automaton-runner auth");
                return;
            }

            await SetupHttpClientAuth();

            // Get runner name if not provided
            if (string.IsNullOrEmpty(runnerName))
            {
                var currentName = configService.ApplicationName;
                var prompt = !string.IsNullOrEmpty(currentName) 
                    ? $"Enter runner name (current: {currentName}): " 
                    : "Enter runner name: ";
                    
                Write(prompt);
                runnerName = ReadLine();
                
                if (string.IsNullOrWhiteSpace(runnerName))
                {
                    if (!string.IsNullOrEmpty(currentName))
                    {
                        runnerName = currentName;
                        WriteLine($"Using current name: {runnerName}");
                    }
                    else
                    {
                        runnerName = $"AutoRunner-{Environment.MachineName}";
                        WriteLine($"Using default name: {runnerName}");
                    }
                }
            }

            var success = await RegisterRunnerAsync(runnerName);
            if (success)
            {
                WriteLine($"? Runner '{runnerName}' registered successfully!");
                WriteLine();
                WriteLine("?? Next step: automaton-runner");
            }
            else
            {
                WriteLine($"? Failed to register runner '{runnerName}'");
            }
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error in interactive registration. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Registration error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
        }
    }

    /// <summary>
    /// Configure server URL
    /// </summary>
    public async Task ConfigureServerAsync(string? serverUrl = null)
    {
        try
        {
            WriteLine("=== Automaton Runner - Server Configuration ===");
            WriteLine($"Current server: {configService.BaseUrl ?? "Not configured"}");
            WriteLine();

            if (!string.IsNullOrEmpty(serverUrl))
            {
                configService.UpdateServerUrl(serverUrl);
                WriteLine($"? Server configured: {configService.BaseUrl}");
            }
            else
            {
                Write("Enter new server URL: ");
                var inputServerUrl = ReadLine();
                
                if (string.IsNullOrWhiteSpace(inputServerUrl))
                {
                    WriteLine("Server URL cannot be empty.");
                    return;
                }
                
                configService.UpdateServerUrl(inputServerUrl);
                WriteLine($"? Server configured: {configService.BaseUrl}");
            }

            // Check if we need to re-register due to server change
            if (configService.IsRunnerRegistered())
            {
                WriteLine();
                WriteLine("?? Note: Server URL changed. You may need to re-register your runner.");
                WriteLine("Run: automaton-runner register --force");
            }
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error configuring server. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Server configuration error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
        }
    }

    /// <summary>
    /// Start the runner (after setup is complete)
    /// </summary>
    private async Task StartRunnerAsync()
    {
        WriteLine("?? Starting runner...");
        WriteLine();
        
        // Attempt to connect to hub with better error handling
        WriteLine("?? Establishing connection to server...");
        var connected = await hubService.ConnectAsync();
        
        if (!connected)
        {
            WriteLine();
            WriteLine("?? Unable to establish initial connection to server.");
            WriteLine("   The runner will continue attempting to connect in the background.");
            WriteLine("   You can monitor the connection status by pressing 's' for status.");
            WriteLine();
            WriteLine("? Would you like to:");
            WriteLine("   • Wait for connection (press Enter to continue)");
            WriteLine("   • Exit and fix connection issues (press 'q' to quit)");
            WriteLine();
            
            Write("Your choice (Enter to continue, 'q' to quit): ");
            var choice = ReadLine()?.Trim().ToLower();
            
            if (choice == "q" || choice == "quit")
            {
                WriteLine("?? Exiting runner...");
                return;
            }
            
            WriteLine("? Continuing in disconnected mode - will connect when server becomes available.");
        }

        // Start the runner regardless of initial connection status
        cancellationTokenSource = new CancellationTokenSource();
        
        WriteLine();
        WriteLine("?? Runner is now active and will execute workflows when connected...");
        WriteLine($"?? Runner: {configService.ApplicationName} (ID: {configService.RunnerId})");
        WriteLine($"?? Server: {configService.BaseUrl}");
        WriteLine($"?? Connection Status: {(hubService.IsConnected() ? "Connected" : "Attempting to connect...")}");
        WriteLine();
        WriteLine("??  Controls:");
        WriteLine("   • Press 'q' to quit");
        WriteLine("   • Press 's' for detailed status");
        WriteLine("   • Press 'r' to retry connection");
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
                    case "r":
                        WriteLine("?? Attempting to reconnect...");
                        await AttemptReconnection();
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
        WriteLine("?? Runner stopped");
    }

    private async Task AttemptReconnection()
    {
        try
        {
            WriteLine("?? Attempting manual reconnection...");
            var connected = await hubService.ConnectAsync();
            
            if (connected)
            {
                WriteLine("? Reconnection successful!");
            }
            else
            {
                WriteLine("? Reconnection failed - automatic retry will continue in background.");
            }
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Warning(ex, "Manual reconnection attempt failed. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Manual reconnection failed - automatic retry will continue in background.");
        }
        
        WriteLine();
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
            // Log technical details to file logs only
            logger.Error(ex, "Error during runner registration. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Registration error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
            return false;
        }
    }

    public async Task ShowStatusAsync()
    {
        WriteLine();
        WriteLine("???????????????????????????????????????");
        WriteLine("         ?? RUNNER STATUS REPORT         ");
        WriteLine("???????????????????????????????????????");
        WriteLine();
        
        // Runner Information
        WriteLine("?? Runner Information:");
        WriteLine($"   • ID: {configService.RunnerId}");
        WriteLine($"   • Name: {configService.ApplicationName}");
        WriteLine($"   • Machine: {Environment.MachineName}");
        WriteLine();
        
        // Connection Status
        var isConnected = hubService.IsConnected();
        WriteLine("?? Connection Status:");
        WriteLine($"   • Server: {configService.BaseUrl}");
        WriteLine($"   • Status: {(isConnected ? "? Connected" : "? Disconnected")}");
        
        if (!isConnected)
        {
            WriteLine("   • The runner will automatically attempt to reconnect");
            WriteLine("   • Press 'r' to manually retry connection");
        }
        WriteLine();
        
        // Authentication Status
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        var hasEnvKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
        var hasStoredKey = !string.IsNullOrEmpty(await storageService.LoadApiKeyAsync());
        
        WriteLine("?? Authentication Status:");
        WriteLine($"   • Authenticated: {(!string.IsNullOrEmpty(apiKey) ? "? Yes" : "? No")}");
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            var authSource = hasEnvKey ? "Environment Variable" : hasStoredKey ? "Stored API Key" : "Unknown";
            WriteLine($"   • Auth Source: {authSource}");
            WriteLine($"   • Key Preview: {apiKey[..Math.Min(15, apiKey.Length)]}...");
        }
        WriteLine();
        
        // Runtime Information
        WriteLine("??  Runtime Information:");
        WriteLine($"   • Process ID: {Environment.ProcessId}");
        WriteLine($"   • Working Directory: {Environment.CurrentDirectory}");
        WriteLine($"   • .NET Version: {Environment.Version}");
        WriteLine();
        
        // Available Commands
        WriteLine("??  Available Commands:");
        WriteLine("   • automaton-runner (start runner)");
        WriteLine("   • automaton-runner status (show this status)");
        WriteLine("   • automaton-runner auth (authenticate)");
        WriteLine("   • automaton-runner register (register runner)");
        WriteLine("   • automaton-runner cleanup (clear stored data)");
        
        WriteLine("???????????????????????????????????????");
        WriteLine();
    }

    private async Task<bool> ClearAllDataAsync()
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
            // Log technical details to file logs only
            logger.Error(ex, "Error clearing all data. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Error clearing data: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
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
            // Log technical details to file logs only
            logger.Error(ex, "Error during cleanup. Error: {ErrorType}", ex.GetType().Name);
            
            // Show friendly message to user
            WriteLine($"? Cleanup error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
            return false;
        }
    }
}