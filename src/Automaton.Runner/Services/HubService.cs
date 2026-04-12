using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.StaticFiles;
using Serilog;
using System.Net.Sockets;
using Automaton.Runner.Console.Connection;
using static System.Console;
using Microsoft.Extensions.DependencyInjection;
using Automaton.Runner.Console.Models;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using System.Threading;

namespace Automaton.Runner.Console.Services;

public class HubService
{
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunWorkflowMethod = "RunWorkflow";
    private const string RunCustomStepMethod = "RunCustomStep";
    private const string UploadControllerScriptMethod = "UploadControllerScript";
    private const string TunnelRequestMethod = "TunnelRequest";
    private const string TunnelResponseMethod = "TunnelResponse";
    private const string PingMethod = "Ping";
    private const string ReceiveFileChunkMethod = "ReceiveFileChunk";
    private const string FileUploadCompleteMethod = "FileUploadComplete";
    private const string FileUploadFailedMethod = "FileUploadFailed";
    private const string FetchFileChunkMethod = "FetchFileChunk";
    private const string FileChunkReadyMethod = "FileChunkReady";

    private HubConnection? connection;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly FlowExecutionService flowExecutionService;
    private readonly TunnelForwarderService tunnelForwarder;
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger logger;
    private readonly IConnectionPolicy resilientConnectionPolicy;
    private bool isReconnecting = false;
    private bool shouldKeepRetrying = true;
    private CancellationTokenSource? retryTokenSource;

    public HubService(
        ConfigurationService configService,
        ApiKeyAuthenticationService apiKeyAuthService,
        FlowExecutionService flowExecutionService,
        TunnelForwarderService tunnelForwarder,
        IServiceProvider serviceProvider)
    {
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.flowExecutionService = flowExecutionService;
        this.tunnelForwarder = tunnelForwarder;
        this.serviceProvider = serviceProvider;
        this.logger = Log.ForContext<HubService>();
        
        // Configure resilient connection policy
        this.resilientConnectionPolicy = new ResilientPolicy(
            retryAfterSeconds: 5,
            exceptionsAllowedBeforeBreaking: 3,
            durationOfBreak: TimeSpan.FromMinutes(2)
        );
    }

    public async Task<bool> ConnectAsync()
    {
        CleanupStaleTempFiles();
        try
        {
            var apiKey = await apiKeyAuthService.GetValidApiKeyAsync();
            
            if (string.IsNullOrEmpty(apiKey))
            {
                WriteLine("❌ Not authenticated. Please set an API key first.");
                return false;
            }

            if (!configService.IsRunnerRegistered())
            {
                WriteLine("❌ Runner not registered. Please register first.");
                return false;
            }

            // Get the hub URL dynamically to ensure we use the current BaseUrl
            var hubUrl = $"{configService.BaseUrl}/{configService.WorkflowHubUrl}";
            
            WriteLine($"🔗 Connecting to {hubUrl}...");
            WriteLine($"🔍 API Key: {apiKey[..Math.Min(15, apiKey.Length)]}...");
            WriteLine($"🔍 Runner ID: {configService.RunnerId}");
            WriteLine($"🔍 Runner Name: {configService.ApplicationName}");
            
            // Start continuous retry loop in background
            retryTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => ContinuousRetryLoop(apiKey, hubUrl, retryTokenSource.Token));
            
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error starting connection process");
            return false;
        }
    }
    
    private async Task ContinuousRetryLoop(string apiKey, string hubUrl, CancellationToken cancellationToken)
    {
        var wasDisconnected = false;
        
        while (shouldKeepRetrying && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check if we should stop retrying (e.g., max duration exceeded)
                if (resilientConnectionPolicy.ShouldStopRetrying())
                {
                    WriteLine("⏹️  Maximum retry duration exceeded - stopping connection attempts");
                    logger.Warning("Stopping continuous retry loop - maximum retry duration exceeded");
                    break;
                }
                
                // If already connected, just wait and check again
                if (IsConnected())
                {
                    // Reset the flag if we reconnected
                    if (wasDisconnected)
                    {
                        wasDisconnected = false;
                        isReconnecting = false; // Reset the flag for next cycle
                    }
                    
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                    continue;
                }
                
                wasDisconnected = true;
                
                // Try to connect using the resilient policy (which includes circuit breaker)
                var attempts = await BuildAndConnectWithRetry(apiKey, hubUrl);
                
                // If we get here, connection succeeded
                logger.Information("Successfully connected via continuous retry loop (attempts: {Attempts})", attempts);

                // Conditional console output based on attempts
                if (attempts <= 1)
                {
                    WriteLine("✅ Connected to server successfully");
                }
                else
                {
                    WriteLine($"✅ Connection established successfully after {attempts} attempt{(attempts > 1 ? "s" : "") }!");
                }

                isReconnecting = false;
                wasDisconnected = false;
                
                // Wait a bit before next check (in case connection drops)
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation, exit loop
                break;
            }
            catch (Exception ex)
            {
                // Connection failed (including circuit breaker open)
                await HandleConnectionError(ex);
                
                // Wait before next retry attempt
                // Use longer delay to respect the circuit breaker cool-down period
                var delaySeconds = 30; // Wait 30 seconds between retry cycles
                logger.Debug("Waiting {DelaySeconds} seconds before next connection attempt", delaySeconds);
                
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        logger.Information("Continuous retry loop terminated");
    }

    private async Task<int> BuildAndConnectWithRetry(string apiKey, string hubUrl)
    {
        var attempts = 0;

        await resilientConnectionPolicy.GetPolicy().ExecuteAsync(async () =>
        {
            Interlocked.Increment(ref attempts);
            await BuildAndConnect(apiKey, hubUrl);
        });

        return attempts;
    }

    private async Task BuildAndConnect(string apiKey, string hubUrl)
    {
        try
        {
            // Dispose existing connection if any
            if (connection != null)
            {
                await connection.DisposeAsync();
                connection = null;
            }

            connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.Headers.Add("X-API-Key", apiKey);
                    options.Headers.Add(RunnerIdHeader, configService.RunnerId);
                    options.Headers.Add(RunnerNameHeader, configService.ApplicationName);
                    
                    // Bypass SSL validation in development mode (for self-signed certificates)
                    var isDevelopment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development" ||
                                       Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                    if (isDevelopment)
                    {
                        options.HttpMessageHandlerFactory = (handler) =>
                        {
                            if (handler is HttpClientHandler clientHandler)
                            {
                                clientHandler.ServerCertificateCustomValidationCallback = 
                                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                            }
                            return handler;
                        };
                    }
                })
                .WithAutomaticReconnect(new[] 
                { 
                    TimeSpan.FromSeconds(2), 
                    TimeSpan.FromSeconds(5), 
                    TimeSpan.FromSeconds(10), 
                    TimeSpan.FromSeconds(30) 
                })
                .Build();

            // Increase server timeout for long-running script execution
            // Default is 30 seconds which is too short for HTTP-based Python scripts
            connection.ServerTimeout = TimeSpan.FromMinutes(5);
            
            // Keep-alive interval to maintain connection during long operations
            connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

            // Register workflow execution handler
            // Note: SignalR client handlers with return values must return synchronously.
            // We use Task.Run() to execute async work on a background thread,
            // keeping SignalR's message processing thread free for keep-alives.
            connection.On<Guid, WorkflowExecution>(RunWorkflowMethod, (workflowId) =>
            {
                return ExecuteWorkflowHandler(workflowId);
            });

            // Register custom step execution handler
            connection.On<CustomStepPayload, CustomStepExecutionResponse>(RunCustomStepMethod, (customStep) =>
            {
                return ExecuteCustomStepHandler(customStep);
            });

            // Register script upload handler — runner saves scripts to local disk for FastAPI to read
            connection.On<AppControllerUploadPayload, AppControllerUploadResult>(UploadControllerScriptMethod, (payload) =>
            {
                return UploadControllerScriptHandler(payload);
            });

            // Register tunnel forwarding handler
            connection.On<TunnelRequest>(TunnelRequestMethod, async (request) =>
            {
                logger.Debug("Tunnel request received: {Method} {Path} RequestId={RequestId}",
                    request.Method, request.Path, request.RequestId);
                try
                {
                    var response = await tunnelForwarder.ForwardAsync(request);
                    if (response.StatusCode >= 200 && response.StatusCode < 300)
                        logger.Debug("Tunnel response sent: {StatusCode} RequestId={RequestId}", response.StatusCode, request.RequestId);
                    else
                    {
                        logger.Warning("Tunnel forwarded with non-2xx: {StatusCode} RequestId={RequestId} Error={Error}",
                            response.StatusCode, request.RequestId, response.Error);
                        WriteLine($"⚠️ Controller error ({response.StatusCode}): {response.Error ?? "(no details)"}  [{request.Path}]");
                    }
                    await connection.SendAsync(TunnelResponseMethod, response);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Tunnel forwarding failed: {Method} {Path} RequestId={RequestId}",
                        request.Method, request.Path, request.RequestId);
                }
            });

            connection.On(PingMethod, () =>
            {
                logger.Debug("Received ping from server");
                return "Pong";
            });

            // Handle incoming file upload chunks — write to runner's local volume
            connection.On<FileUploadChunk>(ReceiveFileChunkMethod, async (chunk) =>
            {
                await HandleReceiveFileChunkAsync(chunk);
            });

            // Handle file download chunk requests — read from runner's local volume and send back
            connection.On<FetchFileChunkRequest>(FetchFileChunkMethod, async (request) =>
            {
                await HandleFetchFileChunkAsync(request);
            });

            // Handle connection events
            connection.Closed += async (error) =>
            {
                if (error != null)
                {
                    // Log full technical details to file logs only
                    logger.Warning(error, "Connection closed with error during operation");
                    WriteLine($"⚠️ Connection lost: {GetFriendlyErrorMessage(error)}");
                    
                    if (!isReconnecting)
                    {
                        WriteLine("🔄 Attempting to reconnect...");
                    }
                }
                else
                {
                    logger.Information("Connection closed gracefully");
                    WriteLine("✅ Connection closed");
                }
            };

            connection.Reconnecting += (error) =>
            {
                isReconnecting = true;
                if (error != null)
                {
                    // Log technical details to file logs only
                    logger.Information(error, "Connection reconnecting due to error");
                }
                else
                {
                    logger.Information("Connection reconnecting");
                }
                WriteLine($"🔄 Reconnecting to server: {GetFriendlyErrorMessage(error)}");
                return Task.CompletedTask;
            };

            connection.Reconnected += (connectionId) =>
            {
                isReconnecting = false;
                logger.Information("Reconnected to server with connection ID: {ConnectionId}", connectionId);
                WriteLine("✅ Reconnected to server successfully");
                return Task.CompletedTask;
            };

            await connection.StartAsync();

            // Use logger for success and avoid duplicate console messages — the retry loop will report higher-level status
            logger.Information("Connected to hub at {HubUrl}", hubUrl);
        }
        catch (Exception ex)
        {
            // Log full technical details with stack trace to file logs only
            logger.Error(ex, "Connection attempt failed to hub {HubUrl}. Error type: {ErrorType}, Message: {ErrorMessage}", 
                hubUrl, ex.GetType().Name, ex.Message);
            throw; // Re-throw to let the retry policy handle it
        }
    }

    /// <summary>
    /// Executes workflow on a background thread to avoid blocking SignalR's message loop.
    /// This allows keep-alive pings to continue during long-running operations.
    /// </summary>
    private WorkflowExecution ExecuteWorkflowHandler(Guid workflowId)
    {
        try
        {
            WriteLine($"▶️ Executing workflow: {workflowId}");
            
            // Execute on a background thread to keep SignalR's message thread free
            var task = Task.Run(async () => await flowExecutionService.ExecuteWorkflowAsync(workflowId));
            var result = task.GetAwaiter().GetResult();
            
            WriteLine($"✅ Workflow completed: {result.Status}");
            return result;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing workflow {WorkflowId}", workflowId);
            WriteLine($"❌ Workflow failed: {ex.Message}");

            var failed = new WorkflowExecution(workflowId, configService.ApplicationName)
            {
                Status = WorkflowStatus.Error
            };
            failed.AddLog("Error", ex.Message);
            return failed;
        }
    }

    /// <summary>
    /// Executes custom step on a background thread to avoid blocking SignalR's message loop.
    /// This is crucial for long-running operations like HTTP calls in Python scripts.
    /// </summary>
    private CustomStepExecutionResponse ExecuteCustomStepHandler(CustomStepPayload customStep)
    {
        try
        {
            // Execute on a background thread - crucial for long-running operations
            var task = Task.Run(async () =>
            {
                using var scope = serviceProvider.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<CustomStepExecuteService>();
                return await handler.RunCustomStep(customStep);
            });
            
            var result = task.GetAwaiter().GetResult();
            
            logger.Information("Custom step {CustomStepId} completed with status: {Success}", 
                customStep.Id, result.Success ? "Success" : "Failed");
            
            return result;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error executing custom step {CustomStepId}", customStep.Id);
            WriteLine($"❌ Custom step failed: {ex.Message}");
            
            return new CustomStepExecutionResponse
            {
                Success = false,
                Output = null,
                Error = $"Handler exception: {ex.Message}",
                Started = DateTime.UtcNow,
                Finished = DateTime.UtcNow,
                OutputVariables = []
            };
        }
    }

    /// <summary>
    /// Receives a controller script from the server and writes it to local disk for FastAPI to read.
    /// If the file is requirements.txt, installs packages into the per-app packages directory on the
    /// persistent volume so they survive container recreates.
    /// Returns true on success; server uses this as acknowledgement.
    /// </summary>
    private static string AppsRootDir =>
        Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "data", "apps");

    private AppControllerUploadResult UploadControllerScriptHandler(AppControllerUploadPayload payload)
    {
        try
        {
            var backendDir = Path.Combine(
                AppsRootDir,
                payload.AppId.ToString(), "backend");

            Directory.CreateDirectory(backendDir);

            // requirements.txt — write as plain text, then pip install to volume-backed packages dir
            if (payload.Language == "Text" && payload.ControllerName == "requirements")
            {
                var reqPath = Path.Combine(backendDir, "requirements.txt");
                File.WriteAllText(reqPath, payload.Code);

                var packagesDir = Path.Combine(AppsRootDir, payload.AppId.ToString(), "packages");
                Directory.CreateDirectory(packagesDir);

                var pipLogs = InstallRequirements(payload.AppId, reqPath, packagesDir);
                return new AppControllerUploadResult { Success = true, Logs = pipLogs };
            }

            // system-requirements.txt — write as plain text, then apt-get install (Docker runners only)
            if (payload.Language == "Text" && payload.ControllerName == "system-requirements")
            {
                var sysReqPath = Path.Combine(backendDir, "system-requirements.txt");
                File.WriteAllText(sysReqPath, payload.Code);

                var aptLogs = InstallSystemPackages(payload.AppId, payload.Code);
                return new AppControllerUploadResult { Success = true, Logs = aptLogs };
            }

            // ControllerName may include a relative path (e.g. "shared/db") — create subdirs as needed
            var normalizedName = payload.ControllerName.Replace('/', Path.DirectorySeparatorChar);
            var scriptPath = Path.Combine(backendDir, normalizedName + ".py");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, payload.Code);

            logger.Information("Deployed controller script {ControllerName} for app {AppId}",
                payload.ControllerName, payload.AppId);
            WriteLine($"📦 Deployed controller: {payload.ControllerName} (app {payload.AppId})");

            return new AppControllerUploadResult { Success = true };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to save controller script {ControllerName} for app {AppId}",
                payload.ControllerName, payload.AppId);
            WriteLine($"❌ Failed to deploy controller: {payload.ControllerName} - {ex.Message}");
            return new AppControllerUploadResult { Success = false, Logs = [$"❌ {ex.Message}"] };
        }
    }

    /// <summary>
    /// Returns true when the runner is executing inside a Docker container.
    /// In a container the base filesystem is ephemeral, so packages must be installed
    /// to a volume-backed directory via <c>--target</c> to survive image recreates.
    /// On a standalone machine packages install to the system / venv Python and persist naturally.
    /// </summary>
    private static bool IsRunningInContainer =>
        // .NET SDK images set this; also set by docker-compose.runner.yml explicitly
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
        // Fallback: Linux containers always have this sentinel file
        || File.Exists("/.dockerenv");

    private List<string> InstallRequirements(Guid appId, string requirementsPath, string targetDir)
    {
        var logs = new List<string>();
        try
        {
            var inContainer = IsRunningInContainer;
            var args = inContainer
                ? $"install -r \"{requirementsPath}\" --target \"{targetDir}\" --no-color --progress-bar off --disable-pip-version-check"
                : $"install -r \"{requirementsPath}\" --no-color --progress-bar off --disable-pip-version-check";

            var installMode = inContainer ? $"volume target: {targetDir}" : "system Python";
            logs.Add($"📦 Installing packages ({installMode})...");
            WriteLine($"📦 Installing packages for app {appId} ({installMode})...");
            logger.Information("Installing packages for app {AppId} from {ReqPath} (container={InContainer})",
                appId, requirementsPath, inContainer);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pip",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start pip process");

            // Read stdout and stderr concurrently to avoid deadlock when output buffers fill
            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            process.WaitForExit(120_000); // 2-minute timeout
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                logs.Add(line);

            if (!string.IsNullOrWhiteSpace(stderr))
                foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    logs.Add($"⚠️ {line}");

            if (process.ExitCode == 0)
            {
                logs.Add("✅ Packages installed successfully");
                logger.Information("Packages installed for app {AppId}", appId);
                WriteLine($"✅ Packages installed for app {appId}");
            }
            else
            {
                logs.Add($"❌ pip install failed (exit code {process.ExitCode})");
                logger.Warning("pip install exited {ExitCode} for app {AppId}: {Error}",
                    process.ExitCode, appId, stderr);
                WriteLine($"⚠️ Package install warnings for app {appId}: {stderr}");
            }
        }
        catch (Exception ex)
        {
            logs.Add($"⚠️ pip unavailable: {ex.Message}");
            logger.Error(ex, "Failed to run pip install for app {AppId}", appId);
            WriteLine($"⚠️ pip unavailable — packages not installed for app {appId}: {ex.Message}");
        }
        return logs;
    }

    /// <summary>
    /// Installs system (apt-get) packages declared in system-requirements.txt.
    /// Only works on Docker runners; silently skips with a warning on non-container deployments.
    /// Packages install to the container filesystem and are reinstalled by docker-entrypoint.sh on container restart.
    /// </summary>
    private List<string> InstallSystemPackages(Guid appId, string packageListContent)
    {
        var logs = new List<string>();
        if (!IsRunningInContainer)
        {
            logs.Add("⚠️ system-requirements.txt is only supported on Docker runners — skipped on this runner");
            logger.Warning("system-requirements.txt skipped for app {AppId} — not running in a container", appId);
            return logs;
        }

        var packages = packageListContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l))
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^[a-zA-Z0-9][a-zA-Z0-9\-_.+]*$"))
            .ToList();

        if (packages.Count == 0)
        {
            logs.Add("ℹ️ No system packages to install");
            return logs;
        }

        var packageList = string.Join(" ", packages);
        logs.Add($"🔧 Installing system packages: {packageList}");
        WriteLine($"🔧 Installing system packages for app {appId}: {packageList}");
        logger.Information("Installing system packages for app {AppId}: {Packages}", appId, packageList);

        try
        {
            // Step 1: update package index
            var updatePsi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = "apt-get update -qq",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var updateProc = System.Diagnostics.Process.Start(updatePsi)
                ?? throw new InvalidOperationException("Failed to start apt-get update"))
            {
                updateProc.WaitForExit(60_000);
            }

            // Step 2: install packages (sudo -E preserves DEBIAN_FRONTEND from the process environment)
            var installPsi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = $"-E apt-get install -y --no-install-recommends {packageList}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            installPsi.Environment["DEBIAN_FRONTEND"] = "noninteractive";

            using var process = System.Diagnostics.Process.Start(installPsi)
                ?? throw new InvalidOperationException("Failed to start apt-get install");

            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            process.WaitForExit(300_000); // 5-minute timeout
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                logs.Add(line);

            if (!string.IsNullOrWhiteSpace(stderr))
                foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    logs.Add($"⚠️ {line}");

            if (process.ExitCode == 0)
            {
                logs.Add("✅ System packages installed successfully");
                logger.Information("System packages installed for app {AppId}", appId);
                WriteLine($"✅ System packages installed for app {appId}");
            }
            else
            {
                logs.Add($"❌ apt-get install failed (exit code {process.ExitCode})");
                logger.Warning("apt-get install exited {ExitCode} for app {AppId}", process.ExitCode, appId);
                WriteLine($"⚠️ System package install failed for app {appId}");
            }
        }
        catch (Exception ex)
        {
            logs.Add($"⚠️ apt-get unavailable: {ex.Message}");
            logger.Error(ex, "Failed to run apt-get for app {AppId}", appId);
            WriteLine($"⚠️ apt-get unavailable for app {appId}: {ex.Message}");
        }

        return logs;
    }

    private async Task HandleConnectionError(Exception ex)
    {
        // Get current hub URL for error reporting
        var hubUrl = $"{configService.BaseUrl}/{configService.WorkflowHubUrl}";
        
        // Log full technical details with stack trace to file logs only
        logger.Error(ex, "Connection attempt failed. Server: {Server}, HubUrl: {HubUrl}, Error: {ErrorType}", 
            configService.BaseUrl, hubUrl, ex.GetType().Name);
        
        var friendlyMessage = GetFriendlyErrorMessage(ex);
        
        // Don't flood console with repeated circuit breaker messages
        if (ex.Message.Contains("circuit is now open"))
        {
            // Circuit breaker is open - just log once
            if (!isReconnecting)
            {
                WriteLine($"⏸️  Connection paused temporarily due to repeated failures");
                WriteLine($"   The runner will automatically retry after the cool-down period");
                isReconnecting = true; // Use this flag to prevent repeated messages
            }
            return;
        }
        
        WriteLine($"❌ Connection failed: {friendlyMessage}");
        WriteLine();
        
        // Provide specific troubleshooting guidance
        switch (ex)
        {
            case HttpRequestException httpEx when httpEx.Message.Contains("refused"):
            case SocketException sockEx when sockEx.SocketErrorCode == SocketError.ConnectionRefused:
                WriteLine("🔍 Connection refused - this usually means:");
                WriteLine("   • The Automaton server is not running");
                WriteLine("   • The server URL is incorrect");
                WriteLine("   • A firewall is blocking the connection");
                WriteLine();
                WriteLine("💡 Troubleshooting steps:");
                WriteLine($"   • Check if the server is running at: {configService.BaseUrl}");
                WriteLine("   • Verify the server URL is correct");
                WriteLine("   • Check network connectivity and firewall settings");
                break;
                
            case HttpRequestException httpEx when httpEx.Message.Contains("timeout"):
            case TaskCanceledException when ex.Message.Contains("timeout"):
                WriteLine("🔍 Connection timeout - this usually means:");
                WriteLine("   • The server is slow to respond");
                WriteLine("   • Network connectivity issues");
                WriteLine("   • Server is under heavy load");
                WriteLine();
                WriteLine("💡 The runner will automatically retry connecting...");
                break;
                
            case HttpRequestException httpEx when httpEx.Message.Contains("404") || httpEx.Message.Contains("Not Found"):
                WriteLine("🔍 Hub endpoint not found - this usually means:");
                WriteLine("   • The SignalR hub is not enabled on the server");
                WriteLine("   • The server URL path is incorrect");
                WriteLine($"   • Expected hub path: {hubUrl}");
                break;
                
            case HttpRequestException httpEx when httpEx.Message.Contains("401") || httpEx.Message.Contains("Unauthorized"):
                WriteLine("🔍 Authentication failed - this usually means:");
                WriteLine("   • The API key is invalid or expired");
                WriteLine("   • The API key doesn't have the required permissions");
                WriteLine();
                WriteLine("💡 Try running: automaton-runner auth --api-key your_key_here");
                break;
                
            default:
                WriteLine("💡 General troubleshooting steps:");
                WriteLine("   • Verify the server is running and accessible");
                WriteLine("   • Check your API key and authentication");
                WriteLine("   • Confirm network connectivity");
                WriteLine($"   • Test server connectivity: {configService.BaseUrl}");
                break;
        }
        
        WriteLine();
        WriteLine("🔄 The runner will continue trying to connect automatically...");
        WriteLine("   Press 'q' to quit or wait for connection to be established.");
    }

    private static string GetFriendlyErrorMessage(Exception? ex)
    {
        return ex switch
        {
            null => "Unknown error",
            HttpRequestException httpEx when httpEx.Message.Contains("refused") => "Server connection refused",
            SocketException sockEx when sockEx.SocketErrorCode == SocketError.ConnectionRefused => "Connection refused by server",
            HttpRequestException httpEx when httpEx.Message.Contains("timeout") => "Connection timeout", 
            TaskCanceledException when ex.Message.Contains("timeout") => "Connection timeout",
            HttpRequestException httpEx when httpEx.Message.Contains("404") => "Hub endpoint not found",
            HttpRequestException httpEx when httpEx.Message.Contains("401") => "Authentication failed",
            _ => ex.Message
        };
    }

    public async Task DisconnectAsync()
    {
        // Stop the retry loop
        shouldKeepRetrying = false;
        retryTokenSource?.Cancel();
        
        if (connection != null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Error during disconnect");
            }
            finally
            {
                connection = null;
                WriteLine("📡 Disconnected from server");
            }
        }
    }

    public bool IsConnected()
    {
        return connection?.State == HubConnectionState.Connected;
    }

    private async Task HandleReceiveFileChunkAsync(FileUploadChunk chunk)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "uploads", chunk.SessionId);
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, chunk.FileName);

            await using var fs = new FileStream(filePath,
                chunk.ChunkIndex == 0 ? FileMode.Create : FileMode.Append,
                FileAccess.Write, FileShare.None);
            await fs.WriteAsync(chunk.Data);

            logger.Debug("Upload chunk {Index} written: SessionId={SessionId} IsLast={IsLast}",
                chunk.ChunkIndex, chunk.SessionId, chunk.IsLast);

            if (chunk.IsLast)
            {
                logger.Information("Upload complete: SessionId={SessionId} Path={Path}", chunk.SessionId, filePath);
                await connection!.SendAsync(FileUploadCompleteMethod, new FileUploadCompleteMessage
                {
                    SessionId = chunk.SessionId,
                    LocalPath = filePath
                });
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to handle upload chunk: SessionId={SessionId}", chunk.SessionId);
            try
            {
                await connection!.SendAsync(FileUploadFailedMethod, new FileUploadFailedMessage
                {
                    SessionId = chunk.SessionId,
                    Error = $"Runner failed to write upload chunk: {ex.Message}"
                });
            }
            catch (Exception notifyEx)
            {
                logger.Error(notifyEx, "Failed to notify server of upload failure: SessionId={SessionId}", chunk.SessionId);
            }
        }
    }

    private async Task HandleFetchFileChunkAsync(FetchFileChunkRequest request)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "results", request.SessionId);
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir) : [];

            if (files.Length == 0)
            {
                logger.Warning("No result file found for download: SessionId={SessionId}", request.SessionId);
                await connection!.SendAsync(FileChunkReadyMethod, new FileChunkReadyMessage
                {
                    SessionId = request.SessionId,
                    Data = [],
                    IsLast = true,
                    FileName = "error",
                    ContentType = "application/octet-stream"
                });
                return;
            }

            var filePath = files[0];
            var fileName = Path.GetFileName(filePath);
            var contentType = GetContentType(fileName);

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(request.Offset, SeekOrigin.Begin);

            var buffer = new byte[request.ChunkSize];
            var bytesRead = await fs.ReadAsync(buffer);
            var isLast = fs.Position >= fs.Length;
            var data = bytesRead == request.ChunkSize ? buffer : buffer[..bytesRead];

            logger.Debug("Download chunk sent: SessionId={SessionId} Offset={Offset} Bytes={Bytes} IsLast={IsLast}",
                request.SessionId, request.Offset, bytesRead, isLast);

            await connection!.SendAsync(FileChunkReadyMethod, new FileChunkReadyMessage
            {
                SessionId = request.SessionId,
                Data = data,
                IsLast = isLast,
                FileName = fileName,
                ContentType = contentType
            });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to handle download chunk request: SessionId={SessionId}", request.SessionId);
        }
    }

    private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    private static string GetContentType(string fileName)
    {
        if (_contentTypeProvider.TryGetContentType(fileName, out var contentType))
            return contentType;
        return "application/octet-stream";
    }

    private static void CleanupStaleTempFiles()
    {
        foreach (var baseDir in new[]
        {
            Path.Combine(Path.GetTempPath(), "uploads"),
            Path.Combine(Path.GetTempPath(), "results")
        })
        {
            if (!Directory.Exists(baseDir)) continue;
            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                try
                {
                    var created = Directory.GetCreationTimeUtc(dir);
                    if (DateTime.UtcNow - created > TimeSpan.FromHours(1))
                        Directory.Delete(dir, recursive: true);
                }
                catch { /* ignore cleanup errors */ }
            }
        }
    }
}