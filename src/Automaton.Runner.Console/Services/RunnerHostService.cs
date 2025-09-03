using Serilog;
using System.Text;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

public class RunnerHostService
{
    private readonly ConfigurationService configService;
    private readonly FileStorageService storageService;
    private readonly ConsoleAuthenticationService authService;
    private readonly HubService hubService;
    private readonly HttpClient httpClient;
    private readonly ILogger logger;

    private CancellationTokenSource? cancellationTokenSource;

    public RunnerHostService(
        ConfigurationService configService,
        FileStorageService storageService,
        ConsoleAuthenticationService authService,
        HubService hubService,
        HttpClient httpClient)
    {
        this.configService = configService;
        this.storageService = storageService;
        this.authService = authService;
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

            System.Console.WriteLine("=== Automaton Runner ===");
            System.Console.WriteLine($"Server: {configService.BaseUrl}");
            System.Console.WriteLine();

            // Enhanced authentication check - validate token against server
            if (!await EnsureValidAuthenticationAsync())
            {
                System.Console.WriteLine("? Authentication failed. Cannot start runner.");
                return;
            }

            // Check if runner is registered AND if the name matches
            var existingRunnerId = storageService.GetRunnerId();
            var existingRunnerName = storageService.GetRunnerName();
            
            if (!storageService.IsRunnerRegistered() || existingRunnerName != runnerName)
            {
                if (!string.IsNullOrEmpty(existingRunnerId) && existingRunnerName != runnerName)
                {
                    System.Console.WriteLine($"?? Runner name changed from '{existingRunnerName}' to '{runnerName}'");
                    System.Console.WriteLine("Re-registering runner with new name...");
                }
                else
                {
                    System.Console.WriteLine("?? Runner registration required...");
                }
                
                System.Console.WriteLine($"Registering runner '{runnerName}' with the server.");
                System.Console.WriteLine();
                
                var registerSuccess = await RegisterRunnerAsync(runnerName);
                if (!registerSuccess)
                {
                    System.Console.WriteLine("? Runner registration failed. Cannot start runner.");
                    return;
                }
                
                System.Console.WriteLine("? Runner registration successful!");
                System.Console.WriteLine();
            }
            else
            {
                System.Console.WriteLine($"? Runner '{runnerName}' is already registered (ID: {existingRunnerId})");
                System.Console.WriteLine();
            }

            // Connect to hub
            var connected = await hubService.ConnectAsync();
            if (!connected)
            {
                System.Console.WriteLine("? Failed to connect to server. Cannot start runner.");
                return;
            }

            // Start the runner
            cancellationTokenSource = new CancellationTokenSource();
            
            System.Console.WriteLine();
            System.Console.WriteLine("?? Runner is now active and waiting for workflows...");
            System.Console.WriteLine("Press 'q' to quit, 's' for status");
            System.Console.WriteLine();

            // Handle console input
            _ = Task.Run(async () =>
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var key = System.Console.ReadKey(true);
                    switch (key.KeyChar.ToString().ToLower())
                    {
                        case "q":
                            System.Console.WriteLine("?? Shutting down runner...");
                            cancellationTokenSource.Cancel();
                            break;
                        case "s":
                            ShowStatus();
                            break;
                    }
                }
            });

            // Keep running until cancelled
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }

            await hubService.DisconnectAsync();
            System.Console.WriteLine("? Runner stopped");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error starting runner");
            System.Console.WriteLine($"? Error starting runner: {ex.Message}");
        }
    }

    public async Task<bool> RegisterAsync(string runnerName, string? serverUrl = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(serverUrl))
            {
                configService.UpdateServerUrl(serverUrl);
            }

            System.Console.WriteLine("=== Runner Registration ===");
            System.Console.WriteLine($"Server: {configService.BaseUrl}");
            System.Console.WriteLine($"Runner Name: {runnerName}");

            // Enhanced authentication check - validate token against server
            if (!await EnsureValidAuthenticationAsync())
            {
                return false;
            }

            return await RegisterRunnerAsync(runnerName);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during runner registration");
            System.Console.WriteLine($"? Registration error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> RegisterRunnerAsync(string runnerName)
    {
        try
        {
            var accessToken = await authService.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                System.Console.WriteLine("? No valid authentication token available");
                return false;
            }

            httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var runnerId = Guid.NewGuid().ToString();
            var runnerDetails = new
            {
                Id = runnerId,
                Name = runnerName
            };

            var json = JsonSerializer.Serialize(runnerDetails);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            System.Console.WriteLine("?? Registering with server...");

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/{configService.RunnersUrl}", content);

            if (response.IsSuccessStatusCode)
            {
                await storageService.SaveRunnerConfigAsync(runnerId, runnerName);
                System.Console.WriteLine($"? Runner registered successfully!");
                System.Console.WriteLine($"  Runner ID: {runnerId}");
                System.Console.WriteLine($"  Runner Name: {runnerName}");
                
                logger.Information("Runner {RunnerName} registered with ID {RunnerId}", runnerName, runnerId);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            System.Console.WriteLine($"? Registration failed: {response.StatusCode}");
            if (!string.IsNullOrEmpty(errorContent))
            {
                System.Console.WriteLine($"Error: {errorContent}");
            }

            logger.Warning("Runner registration failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error registering runner");
            System.Console.WriteLine($"? Registration error: {ex.Message}");
            return false;
        }
    }

    private void ShowStatus()
    {
        System.Console.WriteLine();
        System.Console.WriteLine("=== Runner Status ===");
        System.Console.WriteLine($"Runner ID: {storageService.GetRunnerId() ?? "Not set"}");
        System.Console.WriteLine($"Runner Name: {storageService.GetRunnerName() ?? "Not set"}");
        System.Console.WriteLine($"Server: {configService.BaseUrl}");
        System.Console.WriteLine($"Connected: {hubService.IsConnected}");
        System.Console.WriteLine($"Registered: {storageService.IsRunnerRegistered()}");
        
        // Check if we have valid authentication (not just if token exists)
        var hasToken = storageService.IsLoggedIn();
        if (hasToken)
        {
            // Note: We can't easily check token validity synchronously here without making this async
            // So we'll show both pieces of information
            System.Console.WriteLine($"Authenticated: Yes (token stored)");
            System.Console.WriteLine($"Note: Token validity checked during operations");
        }
        else
        {
            System.Console.WriteLine($"Authenticated: No");
        }
        
        System.Console.WriteLine();
    }

    /// <summary>
    /// Ensures we have valid authentication by testing token against server
    /// </summary>
    private async Task<bool> EnsureValidAuthenticationAsync()
    {
        try
        {
            // First check if we have any token stored
            if (!storageService.IsLoggedIn())
            {
                System.Console.WriteLine("?? Authentication required...");
                System.Console.WriteLine("You need to login before running the runner.");
                System.Console.WriteLine();
                
                return await authService.LoginAsync();
            }

            // Test if current token works by making a simple authenticated request
            var validToken = await authService.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(validToken))
            {
                System.Console.WriteLine("?? Token expired, authentication required...");
                System.Console.WriteLine();
                
                return await authService.LoginAsync();
            }

            // Test the token against the server with a simple API call
            if (!await TestTokenValidityAsync(validToken))
            {
                System.Console.WriteLine("?? Token invalid, authentication required...");
                System.Console.WriteLine();
                
                // Clear invalid credentials
                await storageService.DeleteCredentialsAsync();
                
                return await authService.LoginAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Error during authentication validation");
            System.Console.WriteLine("?? Authentication validation failed, login required...");
            System.Console.WriteLine();
            
            return await authService.LoginAsync();
        }
    }

    /// <summary>
    /// Test token validity by making a simple authenticated request to the server
    /// </summary>
    private async Task<bool> TestTokenValidityAsync(string token)
    {
        try
        {
            using var testClient = new HttpClient();
            testClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Make a simple request to test authentication (we'll use the runners endpoint)
            var response = await testClient.GetAsync($"{configService.BaseUrl}/{configService.RunnersUrl}");
            
            // If we get 401, token is invalid; any other response (200, 403, etc.) means token is valid
            return response.StatusCode != System.Net.HttpStatusCode.Unauthorized;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Token validation test failed");
            return false;
        }
    }

    /// <summary>
    /// Clear runner registration from storage
    /// </summary>
    public async Task<bool> ClearRunnerRegistrationAsync()
    {
        try
        {
            await storageService.DeleteRunnerConfigAsync();
            System.Console.WriteLine("? Runner registration cleared successfully!");
            logger.Information("Runner registration cleared");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error clearing runner registration");
            System.Console.WriteLine($"? Error clearing runner registration: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Clear all stored data (credentials + runner registration)
    /// </summary>
    public async Task<bool> ClearAllDataAsync()
    {
        try
        {
            await storageService.DeleteCredentialsAsync();
            await storageService.DeleteRunnerConfigAsync();
            System.Console.WriteLine("? All stored data cleared successfully!");
            logger.Information("All stored data cleared");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error clearing all data");
            System.Console.WriteLine($"? Error clearing all data: {ex.Message}");
            return false;
        }
    }
}