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

using System.Text;

namespace Automaton.Runner.Console.Services;

public class HubService
{
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunnerVersionHeader = "RunnerVersion";
    private const string RunnerMachineNameHeader = "RunnerMachineName";
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
    private const string ListAppBackendFilesMethod = "ListAppBackendFiles";
    private const string ReadAppBackendFileMethod = "ReadAppBackendFile";
    private const string DeleteAppBackendFileMethod = "DeleteAppBackendFile";
    private const string ReconnectWhenIdleMethod = "ReconnectWhenIdle";
    private const string HeartbeatMethod = "Heartbeat";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

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
    private int activeOperationCount = 0;
    private int reconnectRequested = 0;
    private int reconnectInProgress = 0;
    private CancellationTokenSource? heartbeatTokenSource;
    private Task? heartbeatTask;

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
                logger.Debug("Successfully connected via continuous retry loop (attempts: {Attempts})", attempts);

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
        
        logger.Debug("Continuous retry loop terminated");
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
                StopHeartbeatLoop();
                await connection.DisposeAsync();
                connection = null;
            }

            connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.Headers.Add("X-API-Key", apiKey);
                    options.Headers.Add(RunnerIdHeader, configService.RunnerId);
                    options.Headers.Add(RunnerNameHeader, configService.ApplicationName);
                    options.Headers.Add(RunnerVersionHeader, Environment.Version.ToString());
                    options.Headers.Add(RunnerMachineNameHeader, Environment.MachineName);
                    
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

            // Register workflow execution handler.
            connection.On<Guid, WorkflowExecution>(RunWorkflowMethod, (workflowId) =>
            {
                return ExecuteWorkflowHandler(workflowId);
            });

            // Register custom step execution handler.
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
                BeginRunnerActivity();
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
                finally
                {
                    EndRunnerActivity();
                }
            });

            connection.On(PingMethod, () =>
            {
                logger.Debug("Received ping from server");
                return "Pong";
            });

            connection.On<WelcomeRunnerMessage>("WelcomeRunner", (message) =>
            {
                var apiColor = string.IsNullOrWhiteSpace(message.ApiColor) ? "(unset)" : message.ApiColor;
                logger.Debug(
                    "Runner accepted by API instance {ApiInstanceId} color {ApiColor}",
                    message.ApiInstanceId,
                    apiColor);
                WriteLine($"✅ Accepted by API instance {message.ApiInstanceId} (color: {apiColor})");
            });

            connection.On<string>(ReconnectWhenIdleMethod, (reason) =>
            {
                HandleReconnectWhenIdle(reason);
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

            connection.On<string, AppBackendFilesResponse>(ListAppBackendFilesMethod, (appId) =>
            {
                return ListAppBackendFiles(appId);
            });

            connection.On<string, string, AppBackendFileContentResponse>(ReadAppBackendFileMethod, (appId, path) =>
            {
                return ReadAppBackendFile(appId, path);
            });

            connection.On<string, string, AppBackendFileDeleteResponse>(DeleteAppBackendFileMethod, (appId, path) =>
            {
                return DeleteAppBackendFile(appId, path);
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
                    logger.Debug("Connection closed gracefully");
                    WriteLine("✅ Connection closed");
                }
            };

            connection.Reconnecting += (error) =>
            {
                isReconnecting = true;
                if (error != null)
                {
                    // Log technical details to file logs only
                    logger.Warning(error, "Connection reconnecting due to error");
                }
                else
                {
                    logger.Debug("Connection reconnecting");
                }
                WriteLine($"🔄 Reconnecting to server: {GetFriendlyErrorMessage(error)}");
                return Task.CompletedTask;
            };

            connection.Reconnected += (connectionId) =>
            {
                isReconnecting = false;
                logger.Information("Reconnected to server with connection ID: {ConnectionId}", connectionId);
                WriteLine("✅ Reconnected to server successfully");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var runnerHostService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
                        var reRegistered = await runnerHostService.ReRegisterAfterReconnectAsync();
                        if (!reRegistered)
                        {
                            logger.Warning("Runner re-registration after reconnect returned false");
                            WriteLine("⚠️ Runner re-registration after reconnect failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "Runner re-registration after reconnect failed");
                        WriteLine($"⚠️ Runner re-registration after reconnect failed: {ex.Message}");
                    }

                    await SendHeartbeatAsync();
                });
                return Task.CompletedTask;
            };

            await connection.StartAsync();
            StartHeartbeatLoop();
            await SendHeartbeatAsync();

            // Use logger for success and avoid duplicate console messages — the retry loop will report higher-level status
            logger.Debug("Connected to hub at {HubUrl}", hubUrl);
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
    /// Executes workflow asynchronously so the SignalR callback does not block on sync-over-async waits.
    /// </summary>
    private async Task<WorkflowExecution> ExecuteWorkflowHandler(Guid workflowId)
    {
        BeginRunnerActivity();
        try
        {
            WriteLine($"▶️ Executing workflow: {workflowId}");

            var result = await flowExecutionService.ExecuteWorkflowAsync(workflowId);
            
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
        finally
        {
            EndRunnerActivity();
        }
    }

    /// <summary>
    /// Executes custom steps asynchronously so long-running operations do not rely on sync-over-async waits.
    /// </summary>
    private async Task<CustomStepExecutionResponse> ExecuteCustomStepHandler(CustomStepPayload customStep)
    {
        BeginRunnerActivity();
        try
        {
            using var scope = serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<CustomStepExecuteService>();
            var result = await handler.RunCustomStep(customStep);
            
            if (result.Success)
            {
                logger.Debug(
                    "Custom step {CustomStepId} completed successfully",
                    customStep.Id);
            }
            else
            {
                logger.Warning(
                    "Custom step {CustomStepId} failed: {Error}",
                    customStep.Id,
                    result.Error);
            }
            
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
        finally
        {
            EndRunnerActivity();
        }
    }

    /// <summary>
    /// Receives a controller script from the server and writes it to local disk for FastAPI to read.
    /// Each app gets its own Python virtual environment under the persistent app data directory.
    /// requirements.txt rebuilds that app's venv so removed packages do not linger.
    /// </summary>
    private static string AppsRootDir =>
        Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "data", "apps");

    private AppControllerUploadResult UploadControllerScriptHandler(AppControllerUploadPayload payload)
    {
        try
        {
            var appRootDir = GetAppRootDirectory(payload.AppId);
            var backendDir = Path.Combine(appRootDir, "backend");

            Directory.CreateDirectory(backendDir);

            var relativePath = string.IsNullOrWhiteSpace(payload.RelativePath)
                ? BuildLegacyRelativePath(payload)
                : NormalizeBackendRelativePath(payload.RelativePath);

            // requirements.txt — write as plain text, then rebuild/install the app-specific venv
            if (payload.Language == "Text" && string.Equals(relativePath, "requirements.txt", StringComparison.OrdinalIgnoreCase))
            {
                var reqPath = Path.Combine(backendDir, "requirements.txt");
                File.WriteAllBytes(reqPath, payload.Content);

                return InstallRequirements(payload.AppId, reqPath);
            }

            // system-requirements.txt — write as plain text, then apt-get install (Docker runners only)
            if (payload.Language == "Text" && string.Equals(relativePath, "system-requirements.txt", StringComparison.OrdinalIgnoreCase))
            {
                var sysReqPath = Path.Combine(backendDir, "system-requirements.txt");
                File.WriteAllBytes(sysReqPath, payload.Content);

                var aptLogs = InstallSystemPackages(payload.AppId, Encoding.UTF8.GetString(payload.Content));
                return new AppControllerUploadResult { Success = true, Logs = aptLogs };
            }

            EnsureAppVirtualEnvironment(payload.AppId, recreate: false);

            var filePath = ResolveBackendUploadPath(backendDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllBytes(filePath, payload.Content);

            logger.Debug("Deployed backend file {RelativePath} for app {AppId}",
                relativePath, payload.AppId);
            WriteLine($"📦 Deployed backend file: {relativePath} (app {payload.AppId})");

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

    private static string BuildLegacyRelativePath(AppControllerUploadPayload payload)
    {
        var extension = payload.Language switch
        {
            "Python" => ".py",
            "Batch" => ".bat",
            _ => ".txt"
        };

        return NormalizeBackendRelativePath(payload.ControllerName + extension);
    }

    private static string NormalizeBackendRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string ResolveBackendUploadPath(string backendDir, string relativePath)
    {
        var normalized = NormalizeBackendRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Invalid backend file path.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(backendDir, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(backendDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, backendDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Backend file path must stay inside the app backend directory.");
        }

        return fullPath;
    }

    private static AppBackendFilesResponse ListAppBackendFiles(string appId)
    {
        try
        {
            var backendDir = GetAppBackendDirectory(ParseAppId(appId));
            if (!Directory.Exists(backendDir))
            {
                return new AppBackendFilesResponse { Success = true };
            }

            var files = Directory
                .EnumerateFiles(backendDir, "*", SearchOption.AllDirectories)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    var relativePath = Path.GetRelativePath(backendDir, path).Replace(Path.DirectorySeparatorChar, '/');
                    return new AppBackendFileEntry
                    {
                        Path = relativePath,
                        Name = info.Name,
                        Size = info.Length,
                        LastModifiedUtc = info.LastWriteTimeUtc,
                        IsText = IsTextFile(relativePath)
                    };
                })
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AppBackendFilesResponse
            {
                Success = true,
                Files = files
            };
        }
        catch (Exception ex)
        {
            return new AppBackendFilesResponse { Success = false, Error = ex.Message };
        }
    }

    private static AppBackendFileContentResponse ReadAppBackendFile(string appId, string relativePath)
    {
        try
        {
            var path = ResolveAppBackendFilePath(ParseAppId(appId), relativePath);
            if (!File.Exists(path))
            {
                return new AppBackendFileContentResponse { Success = false, Error = "File not found." };
            }

            var info = new FileInfo(path);
            var normalizedRelativePath = NormalizeRunnerRelativePath(relativePath);
            var isText = IsTextFile(normalizedRelativePath);
            if (!isText)
            {
                return new AppBackendFileContentResponse
                {
                    Success = true,
                    Path = normalizedRelativePath,
                    Size = info.Length,
                    IsText = false,
                    Error = "Binary preview is not supported."
                };
            }

            const long maxPreviewBytes = 1024 * 1024;
            if (info.Length > maxPreviewBytes)
            {
                return new AppBackendFileContentResponse
                {
                    Success = false,
                    Path = normalizedRelativePath,
                    Size = info.Length,
                    IsText = true,
                    Error = "File is larger than the 1 MB preview limit."
                };
            }

            return new AppBackendFileContentResponse
            {
                Success = true,
                Path = normalizedRelativePath,
                Content = File.ReadAllText(path),
                Size = info.Length,
                IsText = true
            };
        }
        catch (Exception ex)
        {
            return new AppBackendFileContentResponse { Success = false, Error = ex.Message };
        }
    }

    private static AppBackendFileDeleteResponse DeleteAppBackendFile(string appId, string relativePath)
    {
        try
        {
            var path = ResolveAppBackendFilePath(ParseAppId(appId), relativePath);
            if (!File.Exists(path))
            {
                return new AppBackendFileDeleteResponse { Success = false, Error = "File not found." };
            }

            File.Delete(path);
            return new AppBackendFileDeleteResponse { Success = true };
        }
        catch (Exception ex)
        {
            return new AppBackendFileDeleteResponse { Success = false, Error = ex.Message };
        }
    }

    private static string GetAppBackendDirectory(Guid appId)
    {
        return Path.GetFullPath(Path.Combine(GetAppRootDirectory(appId), "backend"));
    }

    private static string GetAppRootDirectory(Guid appId)
    {
        return Path.GetFullPath(Path.Combine(AppsRootDir, appId.ToString()));
    }

    private static string GetAppVenvDirectory(Guid appId)
    {
        return Path.Combine(GetAppRootDirectory(appId), "venv");
    }

    private static Guid ParseAppId(string appId)
    {
        if (!Guid.TryParse(appId, out var parsed))
        {
            throw new ArgumentException("Invalid app ID.");
        }

        return parsed;
    }

    private static string ResolveAppBackendFilePath(Guid appId, string relativePath)
    {
        var backendDir = GetAppBackendDirectory(appId);
        var normalizedPath = NormalizeRunnerRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(backendDir, normalizedPath));

        if (!fullPath.StartsWith(backendDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, backendDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path must stay inside the app backend directory.");
        }

        return fullPath;
    }

    private static string NormalizeRunnerRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Invalid backend file path.");
        }

        return normalized;
    }

    private static bool IsTextFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath).ToLowerInvariant();
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();
        return fileName is ".env"
            || extension is ".py" or ".txt" or ".json" or ".md" or ".csv" or ".log" or ".yml" or ".yaml" or ".ini" or ".toml";
    }

    /// <summary>
    /// Returns true when the runner is executing inside a Docker container.
    /// </summary>
    private static bool IsRunningInContainer =>
        // .NET SDK images set this; also set by docker-compose.runner.yml explicitly
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
        // Fallback: Linux containers always have this sentinel file
        || File.Exists("/.dockerenv");

    private AppControllerUploadResult InstallRequirements(Guid appId, string requirementsPath)
    {
        var logs = new List<string>();
        try
        {
            logs.AddRange(EnsureAppVirtualEnvironment(appId, recreate: true));

            var venvDir = GetAppVenvDirectory(appId);
            var pipPath = GetVenvPipPath(venvDir);
            var pythonPath = GetVenvPythonPath(venvDir);
            var installMode = $"venv: {venvDir}";

            logs.Add($"📦 Installing packages ({installMode})...");
            WriteLine($"📦 Installing packages for app {appId} ({installMode})...");
            logger.Debug("Installing packages for app {AppId} from {ReqPath} into {VenvDir}",
                appId, requirementsPath, venvDir);

            var (_, stdout, stderr) = RunProcessCapture(
                pipPath,
                $"install -r \"{requirementsPath}\" --no-color --progress-bar off --disable-pip-version-check",
                timeoutMs: 300_000);

            AppendProcessLogs(logs, stdout, stderr);

            logs.Add("✅ Packages installed successfully");
            logs.Add($"🐍 App virtual environment ready: {pythonPath}");
            logger.Debug("Packages installed for app {AppId} into {VenvDir}", appId, venvDir);
            WriteLine($"✅ Packages installed for app {appId}");
            return new AppControllerUploadResult { Success = true, Logs = logs };
        }
        catch (Exception ex)
        {
            logs.Add($"❌ {ex.Message}");
            logger.Error(ex, "Failed to set up app virtual environment for app {AppId}", appId);
            WriteLine($"⚠️ App dependency install failed for app {appId}: {ex.Message}");
            return new AppControllerUploadResult { Success = false, Logs = logs };
        }
    }

    private List<string> EnsureAppVirtualEnvironment(Guid appId, bool recreate)
    {
        var logs = new List<string>();
        var venvDir = GetAppVenvDirectory(appId);
        var appRootDir = GetAppRootDirectory(appId);

        Directory.CreateDirectory(appRootDir);

        if (recreate && Directory.Exists(venvDir))
        {
            logs.Add($"♻️ Rebuilding app virtual environment: {venvDir}");
            Directory.Delete(venvDir, recursive: true);
        }

        var pythonPath = GetVenvPythonPath(venvDir);
        if (File.Exists(pythonPath))
        {
            logs.Add($"🐍 Using existing app virtual environment: {pythonPath}");
            return logs;
        }

        logs.Add($"🐍 Creating app virtual environment: {venvDir}");
        WriteLine($"🐍 Creating app virtual environment for app {appId}...");
        logger.Debug("Creating app virtual environment for app {AppId} at {VenvDir}", appId, venvDir);

        var (_, stdout, stderr) = RunProcessCapture(
            "python",
            $"-m venv --system-site-packages \"{venvDir}\"",
            timeoutMs: 120_000);

        AppendProcessLogs(logs, stdout, stderr);

        if (!File.Exists(pythonPath))
        {
            throw new InvalidOperationException($"Virtual environment created without python executable at {pythonPath}");
        }

        return logs;
    }

    private static string GetVenvPythonPath(string venvDir)
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(venvDir, "Scripts", "python.exe")
            : Path.Combine(venvDir, "bin", "python");
    }

    private static string GetVenvPipPath(string venvDir)
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(venvDir, "Scripts", "pip.exe")
            : Path.Combine(venvDir, "bin", "pip");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcessCapture(string fileName, string arguments, int timeoutMs)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
        var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"Process timed out after {timeoutMs / 1000}s: {fileName} {arguments}");
        }

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process failed (exit code {process.ExitCode}): {fileName} {arguments}{Environment.NewLine}{stderr}".Trim());
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static void AppendProcessLogs(List<string> logs, string stdout, string stderr)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            logs.Add(line);

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                logs.Add($"⚠️ {line}");
        }
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
        logger.Debug("Installing system packages for app {AppId}: {Packages}", appId, packageList);

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
                logger.Debug("System packages installed for app {AppId}", appId);
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
        StopHeartbeatLoop();
        
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

    private void BeginRunnerActivity()
    {
        Interlocked.Increment(ref activeOperationCount);
    }

    private void EndRunnerActivity()
    {
        var remaining = Interlocked.Decrement(ref activeOperationCount);
        if (remaining < 0)
        {
            Interlocked.Exchange(ref activeOperationCount, 0);
            remaining = 0;
        }

        if (remaining == 0 && Volatile.Read(ref reconnectRequested) == 1)
        {
            QueueReconnectNow("Current work finished.");
        }
    }

    private void HandleReconnectWhenIdle(string? reason)
    {
        var activeOperations = Math.Max(0, Volatile.Read(ref activeOperationCount));
        var message = string.IsNullOrWhiteSpace(reason)
            ? "Server requested reconnect to the new API instance."
            : reason.Trim();

        logger.Warning(
            "Received reconnect-when-idle request from server. ActiveOperations={ActiveOperations} Reason={Reason}",
            activeOperations,
            message);

        if (activeOperations == 0)
        {
            QueueReconnectNow(message);
            return;
        }

        Interlocked.Exchange(ref reconnectRequested, 1);
        WriteLine($"⏳ Reconnect requested by server. Waiting for {activeOperations} active task(s) to finish.");
    }

    private void QueueReconnectNow(string reason)
    {
        if (Interlocked.CompareExchange(ref reconnectInProgress, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref reconnectRequested, 0);
        _ = Task.Run(async () =>
        {
            try
            {
                WriteLine($"🔄 Reconnect requested by server: {reason}");
                await ForceReconnectAsync();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Forced reconnect request failed");
            }
            finally
            {
                Interlocked.Exchange(ref reconnectInProgress, 0);
            }
        });
    }

    private async Task ForceReconnectAsync()
    {
        var currentConnection = connection;
        if (currentConnection == null)
        {
            return;
        }

        StopHeartbeatLoop();

        try
        {
            await currentConnection.StopAsync();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Stopping current SignalR connection during forced reconnect failed");
        }

        try
        {
            await currentConnection.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Disposing current SignalR connection during forced reconnect failed");
        }

        if (ReferenceEquals(connection, currentConnection))
        {
            connection = null;
        }
    }

    private void StartHeartbeatLoop()
    {
        StopHeartbeatLoop();

        heartbeatTokenSource = new CancellationTokenSource();
        heartbeatTask = Task.Run(() => RunHeartbeatLoopAsync(heartbeatTokenSource.Token));
    }

    private void StopHeartbeatLoop()
    {
        heartbeatTokenSource?.Cancel();
        heartbeatTokenSource?.Dispose();
        heartbeatTokenSource = null;
        heartbeatTask = null;
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SendHeartbeatAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendHeartbeatAsync()
    {
        var currentConnection = connection;
        if (currentConnection == null || currentConnection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await currentConnection.SendAsync(HeartbeatMethod);
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Runner heartbeat failed");
        }
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
                logger.Debug("Upload complete: SessionId={SessionId} Path={Path}", chunk.SessionId, filePath);
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
