using Serilog;
using System.Text;
using System.Text.Json;

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

            System.Console.WriteLine("=== Automaton Runner ===");
            System.Console.WriteLine($"Server: {configService.BaseUrl}");
            System.Console.WriteLine();

            // Check API key authentication
            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                System.Console.WriteLine("??  API Key authentication required...");
                System.Console.WriteLine("Options:");
                System.Console.WriteLine("1. Set API key: automaton-runner auth --api-key ak_runner_xxxxxxxxxxxx");
                System.Console.WriteLine("2. Set environment variable: export AUTOMATON_API_KEY=ak_runner_xxxxxxxxxxxx");
                System.Console.WriteLine();
                System.Console.WriteLine("?? Get your API key from Automaton Studio ? Settings ? API Keys");
                return;
            }

            // Set up HTTP client authentication
            await SetupHttpClientAuth();

            // Check if runner is registered
            if (!configService.IsRunnerRegistered())
            {
                System.Console.WriteLine("?? Registering runner...");
                var registerSuccess = await RegisterRunnerAsync(runnerName);
                if (!registerSuccess)
                {
                    System.Console.WriteLine("? Runner registration failed. Cannot start runner.");
                    return;
                }
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
            System.Console.WriteLine("? Runner stopped");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error in runner host service");
            System.Console.WriteLine($"? Error: {ex.Message}");
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

            await SetupHttpClientAuth();

            var success = await RegisterRunnerAsync(runnerName);
            if (success)
            {
                System.Console.WriteLine($"? Runner '{runnerName}' registered successfully");
            }
            else
            {
                System.Console.WriteLine($"? Failed to register runner '{runnerName}'");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error registering runner");
            System.Console.WriteLine($"? Registration error: {ex.Message}");
        }
    }

    private async Task SetupHttpClientAuth()
    {
        httpClient.DefaultRequestHeaders.Clear();
        
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            return;
        }

        throw new InvalidOperationException("No valid API key available for authentication");
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

            var response = await httpClient.PostAsync($"{configService.BaseUrl}/{configService.RunnersUrl}", content);

            if (response.IsSuccessStatusCode)
            {
                await storageService.SaveRunnerConfigAsync(runnerId.ToString(), runnerName);
                logger.Information("Runner {RunnerName} registered with ID {RunnerId}", runnerName, runnerId);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            logger.Warning("Runner registration failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
            System.Console.WriteLine($"Registration failed: {response.StatusCode}");
            if (!string.IsNullOrEmpty(errorContent))
            {
                System.Console.WriteLine($"Error: {errorContent}");
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during runner registration");
            return false;
        }
    }

    private async Task ShowStatusAsync()
    {
        System.Console.WriteLine();
        System.Console.WriteLine("=== Runner Status ===");
        System.Console.WriteLine($"Runner ID: {configService.RunnerId}");
        System.Console.WriteLine($"Runner Name: {configService.ApplicationName}");
        System.Console.WriteLine($"Server: {configService.BaseUrl}");
        System.Console.WriteLine($"Connected: {hubService.IsConnected()}");
        
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        var hasEnvKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
        var hasStoredKey = !string.IsNullOrEmpty(await storageService.LoadApiKeyAsync());
        
        System.Console.WriteLine($"Authenticated: {!string.IsNullOrEmpty(apiKey)}");
        if (!string.IsNullOrEmpty(apiKey))
        {
            System.Console.WriteLine($"Auth Source: {(hasEnvKey ? "Environment Variable" : hasStoredKey ? "Stored API Key" : "Unknown")}");
        }
        System.Console.WriteLine("==================");
        System.Console.WriteLine();
    }
}