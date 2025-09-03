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

            // Check if logged in
            if (!storageService.IsLoggedIn())
            {
                System.Console.WriteLine("?? Authentication required...");
                var loginSuccess = await authService.LoginAsync();
                if (!loginSuccess)
                {
                    System.Console.WriteLine("? Authentication failed. Cannot start runner.");
                    return;
                }
            }

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

            // Check if logged in
            if (!storageService.IsLoggedIn())
            {
                System.Console.WriteLine("?? Authentication required for registration...");
                var loginSuccess = await authService.LoginAsync();
                if (!loginSuccess)
                {
                    return false;
                }
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
        System.Console.WriteLine($"Runner ID: {configService.RunnerId}");
        System.Console.WriteLine($"Runner Name: {configService.ApplicationName}");
        System.Console.WriteLine($"Server: {configService.BaseUrl}");
        System.Console.WriteLine($"Connected: {hubService.IsConnected}");
        System.Console.WriteLine($"Registered: {configService.IsRunnerRegistered()}");
        System.Console.WriteLine($"Authenticated: {storageService.IsLoggedIn()}");
        System.Console.WriteLine();
    }
}