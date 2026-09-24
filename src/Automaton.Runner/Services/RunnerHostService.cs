using Automaton.Core.Scripting.Engines.Python;
using Automaton.Runner.Console.Models;
using Automaton.Runner.Console.Services;
using Serilog;
using System.Text;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Automaton.Core.Enums;
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
    private readonly RunnerWorkspaceService runnerWorkspaceService;

    private readonly RunnerRegistrationWorkflow registrationWorkflow;
    private readonly RunnerLocalFastApiProcess fastApiProcess;
    private readonly RunnerEnrollmentWorkflow enrollmentWorkflow;
    private CancellationTokenSource? cancellationTokenSource;

    public RunnerHostService(
        ConfigurationService configService,
        FileStorageService storageService,
        ApiKeyAuthenticationService apiKeyAuthService,
        HubService hubService,
        HttpClient httpClient,
        ScriptEngineSettingsReporter scriptEngineSettingsReporter,
        RunnerWorkspaceService runnerWorkspaceService)
    {
        this.configService = configService;
        this.storageService = storageService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.hubService = hubService;
        this.httpClient = httpClient;
        this.logger = Log.ForContext<RunnerHostService>();
        this.scriptEngineSettingsReporter = scriptEngineSettingsReporter;
        this.runnerWorkspaceService = runnerWorkspaceService;
        registrationWorkflow = new RunnerRegistrationWorkflow(configService, storageService, httpClient, logger);
        fastApiProcess = new RunnerLocalFastApiProcess(runnerWorkspaceService, logger);
        enrollmentWorkflow = new RunnerEnrollmentWorkflow(configService, storageService, apiKeyAuthService, httpClient, logger);
    }

    /// <summary>
    /// Start in non-interactive service mode - no prompts, use environment variables only
    /// </summary>
    public async Task StartServiceModeAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.Information("Starting runner in service mode (non-interactive)");
            await ApplyInstallationProfileAsync();

            // Validate configuration from environment variables only
            if (!configService.IsServerConfigured())
            {
                logger.Error("Service mode requires AUTOMATON_SERVER_URL environment variable");
                throw new InvalidOperationException("Server URL not configured. Set AUTOMATON_SERVER_URL environment variable.");
            }

            var enrollmentApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
            var runnerPurpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
            await EnsureInfrastructureRunnerRegisteredAsync(runnerPurpose, enrollmentApiKey);

            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            if (RunnerIdentityPolicy.IsManagedHostedRunner(
                    runnerPurpose,
                    Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID")))
            {
                apiKey = await ResolveManagedHostedRunnerCredentialAsync(apiKey);
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                var enrollmentToken = Environment.GetEnvironmentVariable("AUTOMATON_ENROLLMENT_TOKEN");
                if (string.IsNullOrWhiteSpace(enrollmentToken) || !await EnrollAsync())
                {
                    logger.Error("Service mode requires a Runner credential or an enrollment token");
                    throw new InvalidOperationException("Runner authentication is not configured. Set AUTOMATON_ENROLLMENT_TOKEN for first installation or provide a Runner API key.");
                }

                apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            }

            logger.Debug("Service configuration valid - Server: {Server}", configService.BaseUrl);

            // Set up HTTP client authentication
            await SetupHttpClientAuth();

            // Check if runner is registered - if not, use environment variable or default name
            if (!configService.IsRunnerRegistered())
            {
                EnsureManagedHostedRunnerIdentity(runnerPurpose, Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID"));

                logger.Warning("Runner not registered - will register with default name");
                
                // Priority: AUTOMATON_RUNNER_NAME env var > Service-{MachineName}
                var runnerName = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_NAME");
                if (string.IsNullOrEmpty(runnerName))
                {
                    runnerName = $"Service-{Environment.MachineName}";
                }
                
                logger.Debug("Registering runner with name: {RunnerName}", runnerName);
                var registerSuccess = await registrationWorkflow.RegisterRunnerAsync(runnerName);

                if (!registerSuccess)
                {
                    logger.Error("Failed to register runner in service mode");
                    throw new InvalidOperationException("Runner registration failed");
                }
            }

            logger.Information("Runner registered: {Name} (ID: {Id})", configService.ApplicationName, configService.RunnerId);

            // Start the runner without console interaction
            await StartServiceRunnerAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Fatal error in service mode startup");
            throw;
        }
    }

    private async Task EnsureInfrastructureRunnerRegisteredAsync(string runnerPurpose, string? enrollmentApiKey)
    {
        var shouldRegister = string.Equals(runnerPurpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase)
            && !configService.IsRunnerRegistered()
            && !string.IsNullOrWhiteSpace(enrollmentApiKey);
        if (!shouldRegister) return;
        if (await RegisterInfrastructureRunnerAsync(enrollmentApiKey!)) return;
        logger.Error("Infrastructure Runner self-registration failed");
        throw new InvalidOperationException("Infrastructure Runner self-registration failed.");
    }

    private async Task<string?> ResolveManagedHostedRunnerCredentialAsync(string? persistedApiKey)
    {
        var hasPersistedCredential = !string.IsNullOrWhiteSpace(persistedApiKey);
        var validationResult = hasPersistedCredential
            ? await apiKeyAuthService.ValidateRunnerCredentialWithServerAsync(persistedApiKey!)
            : RunnerCredentialValidationResult.Unavailable;
        var enrollmentToken = Environment.GetEnvironmentVariable("AUTOMATON_ENROLLMENT_TOKEN");
        var decision = DecideManagedHostedRunnerCredential(
            hasPersistedCredential,
            validationResult,
            !string.IsNullOrWhiteSpace(enrollmentToken));

        if (decision == ManagedHostedRunnerCredentialDecision.ReusePersistedCredential)
        {
            return persistedApiKey;
        }

        if (decision == ManagedHostedRunnerCredentialDecision.Enroll
            && await EnrollAsync())
        {
            return await apiKeyAuthService.GetValidApiKeyAsync();
        }

        throw new InvalidOperationException(
            "Managed Hosted Runner authentication is unavailable. Re-enroll or reprovision the Hosted Runner.");
    }

    /// <summary>
    /// Start runner loop for service mode - no console interaction
    /// </summary>
    private async Task StartServiceRunnerAsync(CancellationToken stoppingToken)
    {
        logger.Information("Connecting to server...");

        try
        {
            await StartFastApiProcessAsync(stoppingToken);
            await WaitForFastApiAsync(cancellationToken: stoppingToken);
            await EnsureRunnerRegistrationConsistencyAsync();

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

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.Information("Runner service stopping");
        }
        finally
        {
            await hubService.DisconnectAsync();
        }
    }

    /// <summary>
    /// Interactive start - guides user through setup if needed
    /// </summary>
    public async Task StartInteractiveAsync()
    {
        try
        {
            ShowInteractiveSetupHeader();
            await ApplyInstallationProfileAsync();
            await ConfigureInteractiveServerAsync();
            if (!await EnsureInteractiveAuthenticationAsync()) return;
            await SetupHttpClientAuth();
            if (!await EnsureInteractiveRunnerRegistrationAsync()) return;
            await SubmitDetectedPythonSettingsAsync();
            await StartRunnerAsync();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error in interactive runner setup. Error: {ErrorType}", ex.GetType().Name);
            WriteLine($"❌ Setup error: {ex.Message}");
            WriteLine("Check the log files for detailed technical information.");
        }
    }

    private void ShowInteractiveSetupHeader()
    {
        WriteLine("=== Automaton Runner - Interactive Setup ===");
        WriteLine();
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
    }

    private async Task ConfigureInteractiveServerAsync()
    {
        var serverUrl = await storageService.LoadServerUrlAsync();
        if (string.IsNullOrEmpty(serverUrl)) await PromptForServerConfigurationAsync();
        else WriteLine($"✅ Server registered: {serverUrl}");
    }

    private async Task<bool> EnsureInteractiveAuthenticationAsync()
    {
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        var enrollmentToken = Environment.GetEnvironmentVariable("AUTOMATON_ENROLLMENT_TOKEN");
        if (string.IsNullOrEmpty(apiKey) && !string.IsNullOrWhiteSpace(enrollmentToken))
        {
            WriteLine("🔐 Enrolling this Runner using the one-time enrollment token...");
            if (!await EnrollAsync())
            {
                WriteLine("❌ Runner enrollment failed. Check the server URL and enrollment token.");
                return false;
            }
            apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        }
        if (string.IsNullOrEmpty(apiKey)) return await PromptForInteractiveApiKeyAsync();
        var hasEnvironmentKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
        WriteLine($"✅ Authenticated via: {(hasEnvironmentKey ? "Environment Variable" : "Stored API Key")}");
        WriteLine();
        return await ValidateApiKeyWithServerAsync(apiKey) || await HandleInvalidApiKeyAsync();
    }

    private async Task<bool> PromptForInteractiveApiKeyAsync()
    {
        if (ShouldWarnAboutStoredApiKey() && !ConfirmApiKeyEntry()) return false;
        WriteLine("🔧 Authentication required...");
        WriteLine();
        WriteLine("This Runner requires a system-issued Runner credential.");
        WriteLine("Re-enroll or reprovision the Runner from Automaton Studio.");
        WriteLine();
        Write("Enter the system-issued Runner credential (starts with 'ak_runner_'): ");
        var inputApiKey = ReadLine();
        if (string.IsNullOrWhiteSpace(inputApiKey))
        {
            WriteLine("❌ API key is required. Exiting.");
            return false;
        }
        await apiKeyAuthService.SetApiKeyAsync(inputApiKey);
        WriteLine("✅ Authentication configured successfully!");
        WriteLine();
        return true;
    }

    private static bool ShouldWarnAboutStoredApiKey()
        => OperatingSystem.IsWindows()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY", EnvironmentVariableTarget.User));

    private static bool ConfirmApiKeyEntry()
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
        if (response is not ("n" or "no")) return true;
        WriteLine("❌ Authentication required to continue. Exiting.");
        return false;
    }

    private async Task<bool> EnsureInteractiveRunnerRegistrationAsync()
    {
        if (configService.IsRunnerRegistered())
        {
            WriteLine($"✅ Runner registered: {configService.ApplicationName} (ID: {configService.RunnerId})");
            return true;
        }
        WriteLine("🔧 Runner registration required...");
        WriteLine();
        Write("Enter a name for this runner (e.g., 'My-Server-Runner'): ");
        var runnerName = ReadLine();
        if (string.IsNullOrWhiteSpace(runnerName))
        {
            runnerName = $"AutoRunner-{Environment.MachineName}";
            WriteLine($"Using default name: {runnerName}");
        }
        if (await registrationWorkflow.RegisterRunnerAsync(runnerName))
        {
            WriteLine();
            return true;
        }
        WriteLine("❌ Runner registration failed. Cannot start runner.");
        return false;
    }

    private async Task SubmitDetectedPythonSettingsAsync()
    {
        var detected = new PythonEnvironmentDetector().Detect();
        if (detected is not null && detected.IsAutoDetectionSuccessful)
            await scriptEngineSettingsReporter.SubmitPythonSettingsAsync(detected, CancellationToken.None);
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
            await ConfigureAuthenticationServerAsync(serverUrl);
            if (!await ConfigureAuthenticationCredentialAsync(apiKey)) return;
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

    private async Task ConfigureAuthenticationServerAsync(string? serverUrl)
    {
        if (!string.IsNullOrEmpty(serverUrl))
        {
            await configService.UpdateServerUrlAsync(serverUrl);
            WriteLine($"✅ Server configured and saved: {configService.BaseUrl}");
            return;
        }
        if (configService.IsServerConfigured())
        {
            WriteLine($"✅ Using saved server: {configService.BaseUrl}");
            return;
        }
        WriteLine("Server URL is required for authentication.");
        Write($"Enter server URL (or press Enter for {configService.BaseUrl}): ");
        var inputServerUrl = ReadLine();
        if (string.IsNullOrWhiteSpace(inputServerUrl)) inputServerUrl = configService.BaseUrl;
        await configService.UpdateServerUrlAsync(inputServerUrl);
        WriteLine($"✅ Server configured and saved: {configService.BaseUrl}");
    }

    private async Task<bool> ConfigureAuthenticationCredentialAsync(string? apiKey)
    {
        var credential = apiKey;
        if (!string.IsNullOrEmpty(credential))
        {
            await apiKeyAuthService.SetApiKeyAsync(credential);
            WriteLine("✅ Authentication successful!");
        }
        else
        {
            credential = await PromptForNewApiKeyAsync();
            if (credential is null)
            {
                WriteLine("❌ API key is required.");
                return false;
            }
        }
        WriteLine();
        if (await ValidateApiKeyWithServerAsync(credential)) return true;
        return await HandleInvalidApiKeyAsync();
    }

    /// <summary>
    /// Interactive registration
    /// </summary>
    public async Task RegisterInteractiveAsync(string? runnerName = null, bool force = false)
    {
        try
        {
            WriteLine("=== Automaton Runner - Registration ===");
            if (!TryShowRegistrationServer()) return;
            if (!ConfirmRegistrationReplacement(force)) return;
            if (!await EnsureRegistrationAuthenticationAsync()) return;
            await SetupHttpClientAuth();
            var resolvedName = ResolveInteractiveRunnerName(runnerName);
            ReportRegistrationResult(resolvedName, await registrationWorkflow.RegisterRunnerAsync(resolvedName));
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

    private bool TryShowRegistrationServer()
    {
        try
        {
            WriteLine($"Server: {configService.BaseUrl}");
            WriteLine();
            return true;
        }
        catch (InvalidOperationException)
        {
            WriteLine("? Server URL not configured. Please run 'automaton-runner auth --server <url>' first.");
            return false;
        }
    }

    private bool ConfirmRegistrationReplacement(bool force)
    {
        if (!configService.IsRunnerRegistered() || force) return true;
        WriteLine($"? Runner already registered: {configService.ApplicationName} (ID: {configService.RunnerId})");
        WriteLine();
        Write("Do you want to register with a different name? (y/N): ");
        var response = ReadLine()?.Trim().ToLower();
        if (response is "y" or "yes") return true;
        WriteLine("Registration cancelled.");
        return false;
    }

    private async Task<bool> EnsureRegistrationAuthenticationAsync()
    {
        var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
        if (!string.IsNullOrEmpty(apiKey)) return true;
        WriteLine("? Authentication required before registration.");
        WriteLine("Run: automaton-runner auth");
        return false;
    }

    private string ResolveInteractiveRunnerName(string? runnerName)
    {
        if (!string.IsNullOrEmpty(runnerName)) return runnerName;
        var currentName = configService.ApplicationName;
        Write(!string.IsNullOrEmpty(currentName)
            ? $"Enter runner name (current: {currentName}): "
            : "Enter runner name: ");
        var enteredName = ReadLine();
        if (!string.IsNullOrWhiteSpace(enteredName)) return enteredName;
        if (!string.IsNullOrEmpty(currentName))
        {
            WriteLine($"Using current name: {currentName}");
            return currentName;
        }
        var defaultName = $"AutoRunner-{Environment.MachineName}";
        WriteLine($"Using default name: {defaultName}");
        return defaultName;
    }

    private static void ReportRegistrationResult(string runnerName, bool success)
    {
        if (!success)
        {
            WriteLine($"? Failed to register runner '{runnerName}'");
            return;
        }
        WriteLine($"? Runner '{runnerName}' registered successfully!");
        WriteLine();
        WriteLine("?? Next step: automaton-runner");
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

        await StartFastApiProcessAsync(CancellationToken.None);
        await WaitForFastApiAsync(cancellationToken: CancellationToken.None);
        await EnsureRunnerRegistrationConsistencyAsync();

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

    public async Task<bool> ReRegisterAfterReconnectAsync()
    {
        await SetupHttpClientAuth();

        var runnerName = configService.ApplicationName;
        if (string.IsNullOrWhiteSpace(runnerName))
        {
            runnerName = $"AutoRunner-{Environment.MachineName}";
        }

        return await registrationWorkflow.RegisterRunnerAsync(runnerName);
    }

    private async Task EnsureRunnerRegistrationConsistencyAsync()
    {
        if (!ShouldRunGenericIdentityReconciliation(
                Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"),
                Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID")))
        {
            return;
        }

        if (!configService.IsRunnerRegistered())
        {
            return;
        }

        var runnerName = configService.ApplicationName;
        if (string.IsNullOrWhiteSpace(runnerName))
        {
            return;
        }

        try
        {
            var response = await httpClient.GetAsync($"{configService.BaseUrl}/api/runners/by-name/{Uri.EscapeDataString(runnerName)}");

            if (response.IsSuccessStatusCode)
            {
                var runnerDetails = await response.Content.ReadAsAsync<RunnerDetails>();
                if (runnerDetails != null && !string.Equals(configService.RunnerId, runnerDetails.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    await storageService.SaveRunnerConfigAsync(runnerDetails.Id.ToString(), runnerName);
                    logger.Warning("Recovered runner identity for {RunnerName}. Replaced stale local runner ID {OldRunnerId} with server runner ID {RunnerId}", runnerName, configService.RunnerId, runnerDetails.Id);
                    WriteLine($"?? Recovered runner identity from server: {runnerDetails.Id}");
                }

                return;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                logger.Warning("Runner {RunnerName} is registered locally but not found on server. Re-registering before hub connect.", runnerName);
                WriteLine($"?? Runner '{runnerName}' was not found on the server. Re-registering...");

                var registerSuccess = await registrationWorkflow.RegisterRunnerAsync(runnerName, ignoreStoredRunnerId: true);
                if (!registerSuccess)
                {
                    throw new InvalidOperationException($"Failed to re-register runner '{runnerName}' before connecting to the hub.");
                }
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.Warning(ex, "Failed to reconcile runner identity before hub connect for {RunnerName}", runnerName);
        }
    }

    internal static void EnsureManagedHostedRunnerIdentity(string? purpose, string? hostedRunnerId)
    {
        if (RunnerIdentityPolicy.IsManagedHostedRunner(purpose, hostedRunnerId))
        {
            throw new InvalidOperationException("Managed Hosted Runner identity is unavailable. Re-enroll or reprovision the Hosted Runner.");
        }
    }

    internal static bool ShouldRunGenericIdentityReconciliation(string? purpose, string? hostedRunnerId)
        => !RunnerIdentityPolicy.IsManagedHostedRunner(purpose, hostedRunnerId);

    internal static ManagedHostedRunnerCredentialDecision DecideManagedHostedRunnerCredential(
        bool hasPersistedCredential,
        RunnerCredentialValidationResult validationResult,
        bool hasEnrollmentToken)
    {
        if (hasPersistedCredential && validationResult == RunnerCredentialValidationResult.Valid)
        {
            return ManagedHostedRunnerCredentialDecision.ReusePersistedCredential;
        }

        return validationResult == RunnerCredentialValidationResult.Rejected && hasEnrollmentToken
            || !hasPersistedCredential && hasEnrollmentToken
            ? ManagedHostedRunnerCredentialDecision.Enroll
            : ManagedHostedRunnerCredentialDecision.FailClosed;
    }

    internal Task<bool> EnrollAsync()
        => enrollmentWorkflow.EnrollAsync();

    private Task<bool> RegisterInfrastructureRunnerAsync(string enrollmentApiKey)
        => enrollmentWorkflow.RegisterInfrastructureRunnerAsync(enrollmentApiKey);

    private Task ApplyInstallationProfileAsync()
        => enrollmentWorkflow.ApplyInstallationProfileAsync();
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
            logger.Debug("Cleared cached server URL from memory");

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
        WriteLine("Obtain a system-issued Runner credential by enrolling or reprovisioning this Runner from Automaton Studio.");
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

    private Task StartFastApiProcessAsync(CancellationToken cancellationToken = default)
        => fastApiProcess.StartAsync(cancellationToken);

    internal Task WaitForFastApiAsync(int maxWaitSeconds = 30, CancellationToken cancellationToken = default)
        => fastApiProcess.WaitAsync(maxWaitSeconds, cancellationToken);}
