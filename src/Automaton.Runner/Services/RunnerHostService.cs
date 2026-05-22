using Automaton.Core.Scripting.Engines.Python;
using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
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
    private readonly ScriptEngineSettingsReporter scriptEngineSettingsReporter;

    private CancellationTokenSource? cancellationTokenSource;

    public RunnerHostService(
        ConfigurationService configService,
        FileStorageService storageService,
        ApiKeyAuthenticationService apiKeyAuthService,
        HubService hubService,
        HttpClient httpClient,
        ScriptEngineSettingsReporter scriptEngineSettingsReporter)
    {
        this.configService = configService;
        this.storageService = storageService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.hubService = hubService;
        this.httpClient = httpClient;
        this.logger = Log.ForContext<RunnerHostService>();
        this.scriptEngineSettingsReporter = scriptEngineSettingsReporter;
    }

    /// <summary>
    /// Start in non-interactive service mode - no prompts, use environment variables only
    /// </summary>
    public async Task StartServiceModeAsync()
    {
        try
        {
            logger.Information("Starting runner in service mode (non-interactive)");

            // Validate configuration from environment variables only
            if (!configService.IsServerConfigured())
            {
                logger.Error("Service mode requires AUTOMATON_SERVER_URL environment variable");
                throw new InvalidOperationException("Server URL not configured. Set AUTOMATON_SERVER_URL environment variable.");
            }

            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.Error("Service mode requires AUTOMATON_API_KEY environment variable");
                throw new InvalidOperationException("API key not configured. Set AUTOMATON_API_KEY environment variable.");
            }

            logger.Information("Service configuration valid - Server: {Server}", configService.BaseUrl);

            // Set up HTTP client authentication
            await SetupHttpClientAuth();

            // Check if runner is registered - if not, use environment variable or default name
            if (!configService.IsRunnerRegistered())
            {
                logger.Warning("Runner not registered - will register with default name");
                
                // Priority: AUTOMATON_RUNNER_NAME env var > Service-{MachineName}
                var runnerName = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_NAME");
                if (string.IsNullOrEmpty(runnerName))
                {
                    runnerName = $"Service-{Environment.MachineName}";
                }
                
                logger.Information("Registering runner with name: {RunnerName}", runnerName);
                var registerSuccess = await RegisterRunnerAsync(runnerName);

                if (!registerSuccess)
                {
                    logger.Error("Failed to register runner in service mode");
                    throw new InvalidOperationException("Runner registration failed");
                }
            }

            logger.Information("Runner registered: {Name} (ID: {Id})", configService.ApplicationName, configService.RunnerId);

            // Start the runner without console interaction
            await StartServiceRunnerAsync();
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error in service mode startup");
            throw;
        }
    }

    /// <summary>
    /// Start runner loop for service mode - no console interaction
    /// </summary>
    private async Task StartServiceRunnerAsync()
    {
        logger.Information("Connecting to server...");

        try
        {
            await StartFastApiProcessAsync();
            await WaitForFastApiAsync();

            var connected = await hubService.ConnectAsync();

            if (!connected)
            {
                logger.Warning("Initial connection failed - will retry in background");
            }
            else
            {
                logger.Information("Successfully connected to server");
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Connection attempt failed with exception: {ErrorMessage}", ex.Message);
            logger.Error("Exception type: {ExceptionType}", ex.GetType().FullName);
            if (ex.InnerException != null)
            {
                logger.Error("Inner exception: {InnerException}", ex.InnerException.Message);
            }
        }

        logger.Information("Runner active - waiting for workflows");
        logger.Information("Runner: {Name} (ID: {Id})", configService.ApplicationName, configService.RunnerId);
        logger.Information("Server: {Server}", configService.BaseUrl);
        logger.Information("Status: {Status}", hubService.IsConnected() ? "Connected" : "Connecting...");

        // Keep service running indefinitely
        await Task.Delay(Timeout.Infinite);
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

            // DEBUG: Log all environment variables that might affect configuration
            logger.Debug("=== ENVIRONMENT VARIABLES DEBUG ===");
            logger.Debug("AUTOMATON_SERVER_URL: {EnvVar}", Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL"));
            logger.Debug("AUTOMATON_API_KEY: {EnvVar}", string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY")) ? "NOT SET" : "***SET***");
            logger.Debug("ASPNETCORE_ENVIRONMENT: {EnvVar}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
            logger.Debug("DOTNET_ENVIRONMENT: {EnvVar}", Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
            logger.Debug("DOTNET_VERSION: {EnvVar}", Environment.Version);
            logger.Debug("Current Directory: {Dir}", Environment.CurrentDirectory);
            logger.Debug("=== END ENVIRONMENT VARIABLES ===");

            WriteLine("=== ENVIRONMENT VARIABLES DEBUG ===");
            WriteLine("AUTOMATON_SERVER_URL: {0}", Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL"));
            WriteLine("AUTOMATON_API_KEY: {0}", string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY")) ? "NOT SET" : "***SET***");
            WriteLine("ASPNETCORE_ENVIRONMENT: {0}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"));
            WriteLine("DOTNET_ENVIRONMENT: {0}", Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
            WriteLine("DOTNET_VERSION: {0}", Environment.Version);
            WriteLine("Current Directory: {0}", Environment.CurrentDirectory);
            WriteLine("=== END ENVIRONMENT VARIABLES ===");
            WriteLine();

            // Check Server Url
            var serverUrl = await storageService.LoadServerUrlAsync();
            if (string.IsNullOrEmpty(serverUrl))
            {
                // Server not configured and will load the default from appsettings.json
                // Ask user to confirm or change
                var configuredServer = await PromptForServerConfigurationAsync();
            }
            else
            {
                WriteLine($"✅ Server registered: {serverUrl}");
            }

            // Check Authentication
            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey))
            {
                // Check if the API key might be set in registry but not visible to current process
                var isSetInRegistry = OperatingSystem.IsWindows() &&
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY", EnvironmentVariableTarget.User));

                if (isSetInRegistry)
                {
                    WriteLine("⚠️  API key is saved but not visible to this terminal session.");
                    WriteLine();
                    WriteLine("📋 To fix this:");
                    WriteLine("   1. Close this terminal window");
                    WriteLine("   2. Open a new terminal");
                    WriteLine("   3. Run 'automaton-runner' again");
                    WriteLine();
                    WriteLine("   OR set it for current session:");
                    WriteLine("   $env:AUTOMON_API_KEY = [System.Environment]::GetEnvironmentVariable('AUTOMATON_API_KEY', 'User')");
                    WriteLine();
                    Write("Would you like to enter the API key again for this session? (Y/n): ");
                    var response = ReadLine()?.Trim().ToLower();

                    if (response == "n" || response == "no")
                    {
                        WriteLine("❌ Authentication required to continue. Exiting.");
                        return;
                    }
                }

                WriteLine("🔧 Authentication required...");
                WriteLine();
                WriteLine("You need an API key to authenticate with the Automaton server.");
                WriteLine("Get your API key from: Automaton Studio → API Keys");
                WriteLine();

                Write("Enter your API key (starts with 'ak_'): ");
                var inputApiKey = ReadLine();

                if (string.IsNullOrWhiteSpace(inputApiKey))
                {
                    WriteLine("❌ API key is required. Exiting.");
                    return;
                }

                await apiKeyAuthService.SetApiKeyAsync(inputApiKey);
                WriteLine("✅ Authentication configured successfully!");
                WriteLine();
            }
            else
            {
                var hasEnvKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
                WriteLine($"✅ Authenticated via: {(hasEnvKey ? "Environment Variable" : "Stored API Key")}");

                // Validate API key against the server
                WriteLine();
                var isValid = await ValidateApiKeyWithServerAsync(apiKey);

                if (!isValid)
                {
                    var canContinue = await HandleInvalidApiKeyAsync();
                    if (!canContinue)
                    {
                        return;
                    }
                }
            }

            // Set up HTTP client authentication
            await SetupHttpClientAuth();

            // Check/Configure Runner Registration
            if (!configService.IsRunnerRegistered())
            {
                WriteLine("🔧 Runner registration required...");
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
                    WriteLine("❌ Runner registration failed. Cannot start runner.");
                    return;
                }
                WriteLine();
            }
            else
            {
                WriteLine($"✅ Runner registered: {configService.ApplicationName} (ID: {configService.RunnerId})");
            }

            // Start the runner
            // Autodetect Python environment and submit to server
            var detector = new PythonEnvironmentDetector();
            var detected = detector.Detect();
            if (detected != null && detected.IsAutoDetectionSuccessful)
            {
                await scriptEngineSettingsReporter.SubmitPythonSettingsAsync(detected, CancellationToken.None);
            }

            await StartRunnerAsync();
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error in interactive runner setup. Error: {ErrorType}", ex.GetType().Name);

            // Show friendly message to user
            WriteLine($"❌ Setup error: {ex.Message}");
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
                await configService.UpdateServerUrlAsync(serverUrl);
                WriteLine($"✅ Server configured and saved: {configService.BaseUrl}");
            }
            else if (!configService.IsServerConfigured())
            {
                WriteLine("Server URL is required for authentication.");
                Write($"Enter server URL (or press Enter for {configService.BaseUrl}): ");
                var inputServerUrl = ReadLine();

                if (string.IsNullOrWhiteSpace(inputServerUrl))
                {
                    inputServerUrl = $"{configService.BaseUrl}";
                }

                await configService.UpdateServerUrlAsync(inputServerUrl);
                WriteLine($"✅ Server configured and saved: {configService.BaseUrl}");
            }
            else
            {
                WriteLine($"✅ Using saved server: {configService.BaseUrl}");
            }

            // Handle API key authentication
            if (!string.IsNullOrEmpty(apiKey))
            {
                await apiKeyAuthService.SetApiKeyAsync(apiKey);
                WriteLine("✅ Authentication successful!");

                // Validate the provided API key
                WriteLine();
                var isValid = await ValidateApiKeyWithServerAsync(apiKey);
                if (!isValid)
                {
                    var canContinue = await HandleInvalidApiKeyAsync();
                    if (!canContinue)
                    {
                        return;
                    }
                }
            }
            else
            {
                var newApiKey = await PromptForNewApiKeyAsync();
                if (newApiKey == null)
                {
                    WriteLine("❌ API key is required.");
                    return;
                }

                // Validate the entered API key
                WriteLine();
                var isValid = await ValidateApiKeyWithServerAsync(newApiKey);
                if (!isValid)
                {
                    var canContinue = await HandleInvalidApiKeyAsync();
                    if (!canContinue)
                    {
                        return;
                    }
                }
            }

            // Show next steps
            WriteLine();
            WriteLine("📋 Next steps:");
            WriteLine("1. Register runner: automaton-runner register");
            WriteLine("2. Start runner: automaton-runner");
            WriteLine("   (or just run 'automaton-runner' - it will guide you through registration if needed)");
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error in interactive authentication. Error: {ErrorType}", ex.GetType().Name);

            // Show friendly message to user
            WriteLine($"❌ Authentication error: {ex.Message}");
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

            try
            {
                WriteLine($"Server: {configService.BaseUrl}");
            }
            catch (InvalidOperationException)
            {
                WriteLine("? Server URL not configured. Please run 'automaton-runner auth --server <url>' first.");
                return;
            }
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
            WriteLine();

            string configuredServer;

            if (!string.IsNullOrEmpty(serverUrl))
            {
                // Server URL provided as argument
                await configService.UpdateServerUrlAsync(serverUrl);
                configuredServer = configService.BaseUrl;
                WriteLine($"✅ Server configured and saved: {configuredServer}");
            }
            else 
            {
                // Server already configured - ask user to confirm or change
                configuredServer = await PromptForServerConfigurationAsync();
            }

            // Check if we need to re-register due to server change
            if (configService.IsRunnerRegistered())
            {
                WriteLine();
                WriteLine("⚠️ Note: Server URL changed. You may need to re-register your runner.");
                WriteLine("Run: automaton-runner register --force");
            }

            WriteLine();
            WriteLine("💡 The server URL has been saved and will be remembered for future runs.");
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error configuring server. Error: {ErrorType}", ex.GetType().Name);

            // Show friendly message to user
            WriteLine($"❌ Server configuration error: {ex.Message}");
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

        await StartFastApiProcessAsync();
        await WaitForFastApiAsync();

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
        WriteLine($"??? Runner: {configService.ApplicationName} (ID: {configService.RunnerId})");
        WriteLine($"?? Server: {configService.BaseUrl}");
        WriteLine($"?? Connection Status: {(hubService.IsConnected() ? "Connected" : "Attempting to connect...")}");
        WriteLine();

        DisplayControlKeys();

        // Handle console input
        _ = Task.Run(async () =>
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                var key = ReadKey(true);
                switch (key.KeyChar.ToString().ToLower())
                {
                    case "q":
                        ShutDown(cancellationTokenSource);
                        break;
                    case "s":
                        await ShowStatusAsync();
                        DisplayControlKeys();
                        break;
                    case "r":
                        WriteLine("?? Attempting to reconnect...");
                        await AttemptReconnection();
                        DisplayControlKeys();
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

    private void DisplayControlKeys()
    {
        WriteLine("?? Controls:");
        WriteLine("   • Press 'q' to quit");
        WriteLine("   • Press 's' for detailed status");
        WriteLine("   • Press 'r' to retry connection");
        WriteLine();
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

        // Don't clear all headers - just add/update the API key header
        // Clearing headers can remove important headers set elsewhere
        if (httpClient.DefaultRequestHeaders.Contains("X-API-Key"))
        {
            httpClient.DefaultRequestHeaders.Remove("X-API-Key");
        }

        httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        logger.Debug("HTTP client configured with API key authentication (X-API-Key header)");
    }

    private async Task<bool> RegisterRunnerAsync(string runnerName)
    {
        try
        {
            // Check if we have an existing runner ID stored locally
            var existingRunnerId = storageService.GetRunnerId();
            Guid runnerId = Guid.Empty;
            bool isUpdate = false;

            if (!string.IsNullOrEmpty(existingRunnerId) && Guid.TryParse(existingRunnerId, out var parsedId))
            {
                // Reuse existing runner ID for update
                runnerId = parsedId;
                isUpdate = true;
                WriteLine($"?? Found existing runner ID: {runnerId}");
                WriteLine($"?? Updating runner name to: {runnerName}");
            }
            else
            {
                // No local ID - check if runner exists on server by name
                WriteLine($"?? Checking if runner '{runnerName}' already exists on server...");

                try
                {
                    var existingRunnerResponse = await httpClient.GetAsync($"{configService.BaseUrl}/api/runners/by-name/{Uri.EscapeDataString(runnerName)}");

                    if (existingRunnerResponse.IsSuccessStatusCode)
                    {
                        // Runner exists on server! Retrieve its ID
                        var runnerDetails = await existingRunnerResponse.Content.ReadAsAsync<RunnerDetails>();
                        
                       if(runnerDetails != null)
                       {
                            runnerId = runnerDetails.Id;
                            isUpdate = true;
                            WriteLine($"?? Found existing runner on server with ID: {runnerId}");
                            WriteLine($"?? Will reuse this runner locally");

                            // Save to local storage for next time
                            await storageService.SaveRunnerConfigAsync(runnerId.ToString(), runnerName);
                            logger.Information("Runner recovery successful: {RunnerName} (ID: {RunnerId})", runnerName, runnerId);
                            WriteLine($"? Runner '{runnerName}' recovered successfully!");
                            return true;
                        }
                        else
                        {
                            // Couldn't parse runner details, create new runner Id
                            runnerId = Guid.NewGuid();
                            WriteLine($"?? Creating new runner with ID: {runnerId}");
                        }
                    }
                    else
                    {
                        // Runner doesn't exist on server, create new ID
                        runnerId = Guid.NewGuid();
                        WriteLine($"?? Creating new runner with ID: {runnerId}");
                    }
                }
                catch (Exception ex)
                {
                    // If lookup fails, just create new ID and continue
                    logger.Debug(ex, "Could not check for existing runner by name, will create new: {ErrorMessage}", ex.Message);
                    runnerId = Guid.NewGuid();
                    WriteLine($"?? Creating new runner with ID: {runnerId}");
                }
            }

            var runnerData = new
            {
                Id = runnerId,
                Name = runnerName
            };

            var json = JsonSerializer.Serialize(runnerData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;

            if (isUpdate)
            {
                // Update existing runner via PUT
                WriteLine($"?? Making update request to: {configService.BaseUrl}/{configService.RunnersUrl}/{runnerId}");
                WriteLine($"?? Runner data: ID={runnerId}, Name={runnerName}");
                WriteLine($"?? Request headers: X-API-Key = {(httpClient.DefaultRequestHeaders.Contains("X-API-Key") ? "Present" : "Missing")}");

                response = await httpClient.PutAsync($"{configService.BaseUrl}/{configService.RunnersUrl}/{runnerId}", content);
            }
            else
            {
                // Register new runner via POST
                WriteLine($"?? Making registration request to: {configService.BaseUrl}/{configService.RunnersUrl}");
                WriteLine($"?? Runner data: ID={runnerId}, Name={runnerName}");
                WriteLine($"?? Request headers: X-API-Key = {(httpClient.DefaultRequestHeaders.Contains("X-API-Key") ? "Present" : "Missing")}");

                response = await httpClient.PostAsync($"{configService.BaseUrl}/{configService.RunnersUrl}", content);
            }

            WriteLine($"?? Server response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                await storageService.SaveRunnerConfigAsync(runnerId.ToString(), runnerName);
                logger.Information("Runner {RunnerName} {Action} with ID {RunnerId}", runnerName, isUpdate ? "updated" : "registered", runnerId);
                WriteLine($"? Runner {(isUpdate ? "updated" : "registered")} successfully!");
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            logger.Warning("Runner {Action} failed: {StatusCode} - {Error}", isUpdate ? "update" : "registration", response.StatusCode, errorContent);

            WriteLine($"? {(isUpdate ? "Update" : "Registration")} failed: {response.StatusCode}");
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
        WriteLine("╔═════════════════════════════════════════╗");
        WriteLine("         📊 RUNNER STATUS REPORT         ");
        WriteLine("╚═════════════════════════════════════════╝");
        WriteLine();

        // Runner Information
        WriteLine("🤖 Runner Information:");
        WriteLine($"   • ID: {configService.RunnerId}");
        WriteLine($"   • Name: {configService.ApplicationName}");
        WriteLine($"   • Machine: {Environment.MachineName}");
        WriteLine();

        // Connection Status
        var isConnected = hubService.IsConnected();
        WriteLine("🌐 Connection Status:");

        try
        {
            var serverUrl = configService.BaseUrl;
            WriteLine($"   • Server: {serverUrl}");

            // Show server configuration source
            var storedServerUrl = await storageService.LoadServerUrlAsync();
            var isFromStorage = !string.IsNullOrEmpty(storedServerUrl);

            string serverSource = isFromStorage ? "Saved Locally" : "Configuration";
            WriteLine($"   • Source: {serverSource}");
        }
        catch (InvalidOperationException)
        {
            WriteLine("   • Server: ❌ Not configured");
        }

        WriteLine($"   • Status: {(isConnected ? "✅ Connected" : "❌ Disconnected")}");

        if (!isConnected)
        {
            WriteLine("   • The runner will automatically attempt to reconnect");
            WriteLine("   • Press 'r' to manually retry connection");
        }
        WriteLine();

        // Authentication Status
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        var hasEnvKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));

        WriteLine("🔐 Authentication Status:");
        WriteLine($"   • Authenticated: {(!string.IsNullOrEmpty(apiKey) ? "✅ Yes" : "❌ No")}");

        if (!string.IsNullOrEmpty(apiKey))
        {
            WriteLine("   • Auth Source: Environment Variable (AUTOMATON_API_KEY)");
            WriteLine($"   • Key Preview: {apiKey[..Math.Min(15, apiKey.Length)]}...");
        }
        else if (!hasEnvKey)
        {
            WriteLine("   • API Key: ⚠️  Not set. Set AUTOMATON_API_KEY environment variable.");
        }
        WriteLine();

        // Configuration Storage Status
        WriteLine("💾 Stored Configuration:");
        WriteLine($"   • API Key: Environment variable only (AUTOMATON_API_KEY)");
        WriteLine($"   • Runner Config: {(storageService.IsRunnerRegistered() ? "✅ Saved" : "❌ Not saved")}");

        var hasStoredServerUrl = !string.IsNullOrEmpty(await storageService.LoadServerUrlAsync());
        WriteLine($"   • Server URL: {(hasStoredServerUrl ? "✅ Saved" : "❌ Not saved")}");
        WriteLine();

        // Runtime Information
        WriteLine("⚙️ Runtime Information:");
        WriteLine($"   • Process ID: {Environment.ProcessId}");
        WriteLine($"   • Working Directory: {Environment.CurrentDirectory}");
        WriteLine($"   • .NET Version: {Environment.Version}");
        WriteLine();

        // Python Environment Information (Verbose)
        if (VerboseOptions.Verbose)
        {
            var detected = PythonEnvironmentDiagnostics.Detect();
            PythonEnvironmentDiagnostics.PrintIndented(detected);
        }

        // Available Commands
        WriteLine("📋 Available Commands:");
        WriteLine("   • automaton-runner (start runner)");
        WriteLine("   • automaton-runner status (show this status)");
        WriteLine("   • automaton-runner auth (authenticate)");
        WriteLine("   • automaton-runner register (register runner)");
        WriteLine("   • automaton-runner server (configure server)");
        WriteLine("   • automaton-runner service (service management)");
        WriteLine("   • automaton-runner cleanup (clear stored data)");

        WriteLine("╚═════════════════════════════════════════╝");
        WriteLine();
    }

    public void ShutDown(CancellationTokenSource cancellationTokenSource)
    {
        WriteLine("?? Shutting down runner...");
        cancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Cleanup stored API key and runner configuration
    /// </summary>
    public async Task<bool> CleanupAsync()
    {
        try
        {
            WriteLine("🧹 Cleaning up stored runner data...");
            WriteLine();

            var runnerConfigCleared = false;
            var serverConfigCleared = false;

            // Clear runner configuration if it exists
            if (storageService.IsRunnerRegistered())
            {
                await storageService.DeleteRunnerConfigAsync();
                WriteLine("✅ Runner configuration removed");
                runnerConfigCleared = true;
            }

            // Clear server configuration if it exists
            var hasServerConfig = !string.IsNullOrEmpty(await storageService.LoadServerUrlAsync());
            if (hasServerConfig)
            {
                await storageService.DeleteServerConfigAsync();
                WriteLine("✅ Server configuration removed");
                serverConfigCleared = true;
            }

            // Clear cached server URL so it won't be used on next run
            configService.ClearCachedServerUrl();
            logger.Information("Cleared cached server URL from memory");

            if (!runnerConfigCleared && !serverConfigCleared)
            {
                WriteLine("ℹ️  No stored data found to cleanup");
            }
            else
            {
                WriteLine();
                WriteLine("✅ Cleanup completed successfully!");

                if (runnerConfigCleared)
                {
                    WriteLine("   - Runner registration cleared");
                }
                if (serverConfigCleared)
                {
                    WriteLine("   - Server URL configuration cleared");
                }

                WriteLine();
                WriteLine("📝 Note: API key is stored in AUTOMATON_API_KEY environment variable");
                WriteLine("   To remove it, unset the environment variable manually.");
                WriteLine();
                WriteLine("💡 Next time you run 'automaton-runner', you'll be asked to configure everything from scratch.");
                WriteLine("   ℹ️  If you enter a runner name that already exists in the database, the runner will use that existing entry.");
            }

            logger.Information("Runner cleanup completed. Runner config cleared: {RunnerConfigCleared}, Server config cleared: {ServerConfigCleared}",
                runnerConfigCleared, serverConfigCleared);
#if DEBUG

#endif
            return true;
        }
        catch (Exception ex)
        {
            // Log technical details to file logs only
            logger.Error(ex, "Error during cleanup. Error: {ErrorType}", ex.GetType().Name);

            // Show friendly message to user
            WriteLine($"❌ Cleanup error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
            return false;
        }
    }

    /// <summary>
    /// Validate API key against the server
    /// </summary>
    private async Task<bool> ValidateApiKeyWithServerAsync(string apiKey)
    {
        try
        {
            WriteLine("🔐 Validating API key with server...");
            var validationResponse = await httpClient.PostAsync(
                $"{configService.BaseUrl}/api/apikeys/validate",
                new StringContent(JsonSerializer.Serialize(new { ApiKey = apiKey }), Encoding.UTF8, "application/json"));

            if (!validationResponse.IsSuccessStatusCode)
            {
                WriteLine("❌ API key validation failed against the server!");
                WriteLine("   The API key is not valid for this server.");
                return false;
            }

            WriteLine("✅ API key validated successfully!");
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not validate API key against server: {ErrorMessage}", ex.Message);
            WriteLine("⚠️  Could not validate API key against server (connection issue)");
            WriteLine("   Continuing anyway - validation will happen during runner operations");
            return true; // Continue anyway on connection errors
        }
    }

    /// <summary>
    /// Prompt user for a new API key
    /// </summary>
    private async Task<string?> PromptForNewApiKeyAsync()
    {
        WriteLine("Get your API key from: Automaton Studio → API Keys");
        WriteLine();
        Write("Enter your API key (starts with 'ak_'): ");
        var newApiKey = ReadLine();

        if (string.IsNullOrWhiteSpace(newApiKey))
        {
            return null;
        }

        var result = await apiKeyAuthService.SetApiKeyAsync(newApiKey);

        if (!result)
        {
            return null;
        }

        WriteLine("✅ New API key configured!");

        return newApiKey;
    }

    /// <summary>
    /// Handle invalid API key - prompt user for new one or exit
    /// </summary>
    private async Task<bool> HandleInvalidApiKeyAsync()
    {
        WriteLine();
        Write("Would you like to enter a different API key? (Y/n): ");
        var response = ReadLine()?.Trim().ToLower();

        if (response != "n" && response != "no")
        {
            var newApiKey = await PromptForNewApiKeyAsync();
            if (newApiKey == null)
            {
                WriteLine("❌ No API key provided. Exiting.");
                return false;
            }
            return true;
        }
        else
        {
            WriteLine("❌ Cannot continue without a valid API key. Exiting.");
            return false;
        }
    }

    /// <summary>
    /// Shared server configuration logic - prompts user to confirm or change server URL
    /// </summary>
    private async Task<string> PromptForServerConfigurationAsync()
    {
        try
        {
            var server = configService.BaseUrl;

            WriteLine($"✅ Server is: {server}");
            Write("Use this server? (Y/n): ");
            
            var choice = ReadLine()?.Trim().ToLower();

            if (choice == "n" || choice == "new")
            {
                Write("Enter new server URL: ");
                server = ReadLine();

                if (string.IsNullOrWhiteSpace(server))
                {
                    WriteLine("❌ Server URL cannot be empty. Keeping current server.");
                }             
            }

            await configService.UpdateServerUrlAsync(server);
            WriteLine($"✅ Server configured and saved: {server}");
            return server;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Error during server configuration prompt: {ErrorMessage}", ex.Message);
            WriteLine("⚠️  Could not configure server interactively, continuing with current server.");
            return configService.BaseUrl;
        }
    }

    private static bool IsRunningInContainer =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
        || File.Exists("/.dockerenv");

    /// <summary>
    /// Starts the local FastAPI backend process on non-container environments.
    /// FastAPI inherits the runner process environment — AUTOMATON_SERVER_URL, AUTOMATON_API_KEY,
    /// and RUNNER_SCRIPTS_DIR are standard env vars set by the operator.
    /// In Docker, this is a no-op — the entrypoint script already starts FastAPI.
    /// </summary>
    private async Task StartFastApiProcessAsync()
    {
        if (IsRunningInContainer)
        {
            logger.Debug("Running in container — FastAPI managed by entrypoint, skipping launch");
            return;
        }

        // Ask any running FastAPI instance (from a previous runner run) to shut down,
        // then wait until the port is actually free before launching the new one.
        // This is reliable across process restarts — no PID file or static field needed.
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await probe.GetAsync("http://localhost:8001/shutdown");

            // Poll until the port is released (connection refused = old process exited)
            var stopDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < stopDeadline)
            {
                try
                {
                    using var check = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
                    await check.GetAsync("http://localhost:8001/health");
                    await Task.Delay(200); // still bound — keep waiting
                }
                catch
                {
                    break; // connection refused — port is free
                }
            }
        }
        catch { /* not running — fine */ }

        var mainPy = Path.Combine(AppContext.BaseDirectory, "fastapi_server", "main.py");
        if (!File.Exists(mainPy))
        {
            logger.Warning("FastAPI main.py not found at {Path} — skipping auto-launch", mainPy);
            WriteLine($"[!] FastAPI main.py not found at {mainPy} — skipping auto-launch.");
            return;
        }

        // FastAPI inherits the runner's process environment but RUNNER_SCRIPTS_DIR may not be
        // set when running locally (dotnet run). Always inject it explicitly so the fallback
        // /app/data/apps (Docker-only) is never used on a standalone machine.
        var scriptsDir =
            Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data", "apps");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{mainPy}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(mainPy)!
        };
        psi.Environment["RUNNER_SCRIPTS_DIR"] = scriptsDir;

        try
        {
            logger.Information("Launching FastAPI backend with RUNNER_SCRIPTS_DIR={ScriptsDir}", scriptsDir);
            WriteLine($"🔧 FastAPI scripts dir: {scriptsDir}");
            var proc = System.Diagnostics.Process.Start(psi);
            logger.Information("FastAPI backend launched (PID {Pid})", proc?.Id);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to auto-launch FastAPI backend — start it manually");
            WriteLine($"[!] Failed to auto-launch FastAPI backend: {ex.Message}");
            WriteLine("[!] Start it manually: python fastapi_server/main.py");
        }
    }

    /// <summary>
    /// Waits for the local FastAPI backend to become ready before connecting to the server hub.
    /// In Docker, FastAPI starts alongside the .NET runner; this health check ensures it's up.
    /// On bare-metal (no FastAPI), this times out gracefully and logs a warning.
    /// </summary>
    private async Task WaitForFastApiAsync(int maxWaitSeconds = 30)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync("http://localhost:8001/health");
                if (response.IsSuccessStatusCode)
                {
                    if (attempt > 0) WriteLine(); // newline after the dots
                    logger.Information("FastAPI backend ready");
                    return;
                }
            }
            catch { /* not ready yet */ }

            if (attempt == 0)
                Write("⏳ Waiting for FastAPI backend");
            else
                Write(".");

            attempt++;
            await Task.Delay(1000);
        }

        WriteLine(); // newline after the dots
        logger.Warning("FastAPI backend did not become ready within {Seconds}s - tunnel will be unavailable", maxWaitSeconds);
        WriteLine($"[!] FastAPI backend not ready after {maxWaitSeconds}s. App controller execution will not work.");
        WriteLine("[!] Run: pip install fastapi \"uvicorn[standard]\" and restart.");
    }
}
