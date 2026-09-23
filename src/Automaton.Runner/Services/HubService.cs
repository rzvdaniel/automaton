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
using Automaton.Core.Constants;
using System.Threading;

using System.Text;
using System.Reflection;

namespace Automaton.Runner.Console.Services;

public class HubService
{
    internal const string CurrentRunnerApiCompatibilityVersion = RunnerApiCompatibilityVersions.Current;
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunnerVersionHeader = "RunnerVersion";
    private const string RunnerMachineNameHeader = "RunnerMachineName";
    private const string RunnerPurposeHeader = "RunnerPurpose";
    private const string RunnerCapabilitiesHeader = "RunnerCapabilities";
    private const string RunnerHostIdHeader = "RunnerHostId";
    private const string HostedRunnerIdHeader = "HostedRunnerId";
    private const string RunnerImageDigestHeader = "RunnerImageDigest";
    private const string RunnerSpecificationVersionHeader = "RunnerSpecificationVersion";
    private const string RunnerApiCompatibilityVersionHeader = "RunnerApiCompatibilityVersion";
    private const string RunnerOperatingSystemHeader = "RunnerOperatingSystem";
    private const string RunnerArchitectureHeader = "RunnerArchitecture";
    private const string RunnerContainerHealthHeader = "RunnerContainerHealth";
    private const string RunWorkflowMethod = "RunWorkflow";
    private const string RunCustomStepMethod = "RunCustomStep";
    private const string RunInfrastructureCommandMethod = "RunInfrastructureCommand";
    private const string SynchronizeApplicationManagedArtifactsMethod = "SynchronizeApplicationManagedArtifacts";
    private const string TunnelRequestMethod = "TunnelRequest";
    private const string TunnelResponseMethod = "TunnelResponse";
    private const string PingMethod = "Ping";
    private const string ReceiveFileChunkMethod = "ReceiveFileChunk";
    private const string FileUploadCompleteMethod = "FileUploadComplete";
    private const string FileUploadFailedMethod = "FileUploadFailed";
    private const string FetchFileChunkMethod = "FetchFileChunk";
    private const string FileChunkReadyMethod = "FileChunkReady";
    private const string GetAppMediaStatsMethod = "GetAppMediaStats";
    private const string GetAppMediaFileMetadataMethod = "GetAppMediaFileMetadata";
    private const string DeployAppPackageMethod = "DeployAppPackage";
    private const string ExecuteAppActionModuleMethod = "ExecuteAppActionModule";
    private const string CancelAppModuleExecutionMethod = "CancelAppModuleExecution";
    private const string ReadAppModuleExecutionLogsMethod = "ReadAppModuleExecutionLogs";
    private const string ReadAppModuleExecutionResultMethod = "ReadAppModuleExecutionResult";
    private const string ListAppActionModulesMethod = "ListAppActionModules";
    private const string ListAppActionVersionsMethod = "ListAppActionVersions";
    private const string RestoreAppActionVersionMethod = "RestoreAppActionVersion";
    private const string DeleteAppActionVersionMethod = "DeleteAppActionVersion";
    private const string PrepareAppActionModuleMethod = "PrepareAppActionModule";
    private const string SynchronizeAppActionModulesMethod = "SynchronizeAppActionModules";
    private const string CleanupAppDeploymentTargetMethod = "CleanupAppDeploymentTarget";
    private const string ReconnectWhenIdleMethod = "ReconnectWhenIdle";
    private const string ReconnectWhenIdleAcknowledgedMethod = "ReconnectWhenIdleAcknowledged";
    private const string HeartbeatMethod = "Heartbeat";
    private const string ReportHostedRunnerStorageMethod = "ReportHostedRunnerStorage";
    private const string ReportRunnerHostStorageMethod = "ReportRunnerHostStorage";
    private const string RequestHostContainerExecutionMethod = "RequestHostContainerExecution";
    private const string ExecuteHostContainerMethod = "ExecuteHostContainer";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private HubConnection? connection;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly FlowExecutionService flowExecutionService;
    private readonly TunnelForwarderService tunnelForwarder;
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger logger;
    private readonly IConnectionPolicy resilientConnectionPolicy;
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly RunnerDeploymentService runnerDeploymentService;
    private readonly InfrastructureCommandService infrastructureCommandService;
    private readonly HostContainerExecutionService? hostContainerExecutionService;
    private readonly AppActionExecutionService appActionExecutionService;
    private readonly AppActionDeploymentService actionDeploymentService;
    private bool isReconnecting = false;
    private bool shouldKeepRetrying = true;
    private CancellationTokenSource? retryTokenSource;
    private Task? retryTask;
    private int activeOperationCount = 0;
    private int reconnectRequested = 0;
    private int reconnectInProgress = 0;
    private CancellationTokenSource? heartbeatTokenSource;
    private Task? heartbeatTask;
    private readonly SemaphoreSlim heartbeatLifecycleLock = new(1, 1);

    public Task ReportHostedRunnerStorageAsync(HostedRunnerStorageTelemetry report, CancellationToken cancellationToken) =>
        InvokeTelemetryAsync(ReportHostedRunnerStorageMethod, report, cancellationToken);

    public Task ReportRunnerHostStorageAsync(RunnerHostStorageTelemetry report, CancellationToken cancellationToken) =>
        InvokeTelemetryAsync(ReportRunnerHostStorageMethod, report, cancellationToken);

    public async Task<HostContainerExecutionResponse> RequestHostContainerExecutionAsync(
        HostContainerExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (connection?.State != HubConnectionState.Connected)
            throw new InvalidOperationException("Runner is not connected to the API.");
        return await connection.InvokeAsync<HostContainerExecutionResponse>(
            RequestHostContainerExecutionMethod, request, cancellationToken);
    }

    private async Task InvokeTelemetryAsync(string method, object report, CancellationToken cancellationToken)
    {
        var current = connection;
        if (current?.State != HubConnectionState.Connected)
            throw new InvalidOperationException("Runner is not connected to the API.");
        await current.InvokeAsync(method, report, cancellationToken);
    }

    public HubService(
        ConfigurationService configService,
        ApiKeyAuthenticationService apiKeyAuthService,
        FlowExecutionService flowExecutionService,
        TunnelForwarderService tunnelForwarder,
        IServiceProvider serviceProvider,
        RunnerWorkspaceService runnerWorkspaceService,
        RunnerDeploymentService runnerDeploymentService,
        InfrastructureCommandService infrastructureCommandService,
        AppActionExecutionService appActionExecutionService,
        AppActionDeploymentService actionDeploymentService,
        HostContainerExecutionService? hostContainerExecutionService = null)
    {
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.flowExecutionService = flowExecutionService;
        this.tunnelForwarder = tunnelForwarder;
        this.serviceProvider = serviceProvider;
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.runnerDeploymentService = runnerDeploymentService;
        this.infrastructureCommandService = infrastructureCommandService;
        this.appActionExecutionService = appActionExecutionService;
        this.actionDeploymentService = actionDeploymentService;
        this.hostContainerExecutionService = hostContainerExecutionService;
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
        var removedActionArtifacts = appActionExecutionService.CleanupExpiredArtifacts();
        if (removedActionArtifacts > 0)
            logger.Debug("Removed {ArtifactDirectoryCount} expired Action execution artifact directories", removedActionArtifacts);
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
            if (retryTask is { IsCompleted: false })
                return true;
            retryTokenSource?.Dispose();
            retryTokenSource = new CancellationTokenSource();
            shouldKeepRetrying = true;
            var retryToken = retryTokenSource.Token;
            retryTask = Task.Run(() => ContinuousRetryLoop(apiKey, hubUrl, retryToken), CancellationToken.None);
            
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
                var attempts = await BuildAndConnectWithRetry(apiKey, hubUrl, cancellationToken);
                
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

    private async Task<int> BuildAndConnectWithRetry(string apiKey, string hubUrl, CancellationToken cancellationToken)
    {
        var attempts = 0;

        await resilientConnectionPolicy.GetPolicy().ExecuteAsync(async token =>
        {
            Interlocked.Increment(ref attempts);
            await BuildAndConnect(apiKey, hubUrl, token);
        }, cancellationToken);

        return attempts;
    }

    private async Task BuildAndConnect(string apiKey, string hubUrl, CancellationToken cancellationToken)
    {
        try
        {
            // Dispose existing connection if any
            if (connection != null)
            {
                await StopHeartbeatLoopAsync();
                await connection.DisposeAsync();
                connection = null;
            }

            connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.Headers.Add("X-API-Key", apiKey);
                    options.Headers.Add(RunnerIdHeader, configService.RunnerId);
                    options.Headers.Add(RunnerNameHeader, configService.ApplicationName);
                    options.Headers.Add(RunnerVersionHeader, GetRunnerReleaseVersion());
                    options.Headers.Add(RunnerMachineNameHeader, Environment.MachineName);
                    options.Headers.Add(RunnerImageDigestHeader, Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_IMAGE_DIGEST") ?? string.Empty);
                    options.Headers.Add(RunnerSpecificationVersionHeader, Environment.GetEnvironmentVariable("AUTOMATON_CONTAINER_SPECIFICATION_VERSION") ?? string.Empty);
                    options.Headers.Add(RunnerApiCompatibilityVersionHeader, CurrentRunnerApiCompatibilityVersion);
                    options.Headers.Add(RunnerOperatingSystemHeader, System.Runtime.InteropServices.RuntimeInformation.OSDescription);
                    options.Headers.Add(RunnerArchitectureHeader, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
                    var purpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
                    options.Headers.Add(RunnerPurposeHeader, purpose);
                    options.Headers.Add(
                        RunnerCapabilitiesHeader,
                        string.Join(',', RunnerCapabilities.ForInstallationProfile(
                            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_CAPABILITIES")?
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                            purpose)));
                    var runnerHostId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_HOST_ID");
                    if (!string.IsNullOrWhiteSpace(runnerHostId))
                    {
                        options.Headers.Add(RunnerHostIdHeader, runnerHostId);
                    }
                    var hostedRunnerId = Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID");
                    if (!string.IsNullOrWhiteSpace(hostedRunnerId))
                    {
                        options.Headers.Add(HostedRunnerIdHeader, hostedRunnerId);
                    }
                    
                })
                .WithAutomaticReconnect(new JitteredReconnectPolicy())
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

            connection.On<InfrastructureCommandRequest, InfrastructureCommandResponse>(
                RunInfrastructureCommandMethod,
                request => infrastructureCommandService.ExecuteAsync(request, CancellationToken.None));

            connection.On<HostContainerExecutionRequest, HostContainerExecutionResponse>(
                ExecuteHostContainerMethod,
                request => hostContainerExecutionService is null
                    ? Task.FromResult(new HostContainerExecutionResponse { ErrorCode = "unsupported_operation", Error = "Host-container execution is not enabled in this Runner instance." })
                    : hostContainerExecutionService.ExecuteAsync(request, CancellationToken.None));

            connection.On<SynchronizeApplicationManagedArtifactsRequest, SynchronizeApplicationManagedArtifactsResult>(
                SynchronizeApplicationManagedArtifactsMethod,
                request => SynchronizeApplicationManagedArtifacts(request));

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

            connection.On<string>(ReconnectWhenIdleMethod, async (reason) =>
            {
                HandleReconnectWhenIdle(reason);
                try
                {
                    await connection.InvokeAsync(ReconnectWhenIdleAcknowledgedMethod, reason);
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to acknowledge reconnect-when-idle request");
                }
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

            connection.On<string, AppMediaStatsResponse>(GetAppMediaStatsMethod, (appId) =>
            {
                return GetAppMediaStats(appId);
            });

            connection.On<string, string, AppMediaFileMetadataResponse>(GetAppMediaFileMetadataMethod, (appId, relativePath) =>
            {
                return GetAppMediaFileMetadata(appId, relativePath);
            });

            connection.On<DeployAppPackageRequest, DeployAppPackageResult>(DeployAppPackageMethod, async (request) =>
            {
                return await ExecuteWithRunnerActivityAsync(
                    () => runnerDeploymentService.DeployPackage(request));
            });

            connection.On<ExecuteAppActionModuleRequest, AppActionExecutionResult>(ExecuteAppActionModuleMethod, request =>
                appActionExecutionService.ExecuteAsync(request));
            connection.On<CancelAppModuleExecutionRequest, bool>(CancelAppModuleExecutionMethod, request =>
                Task.FromResult(appActionExecutionService.Cancel(request.ExecutionId)));
            connection.On<ReadAppModuleExecutionArtifactRequest, AppModuleExecutionArtifactResponse>(ReadAppModuleExecutionLogsMethod, request =>
                Task.FromResult(appActionExecutionService.ReadLogs(request)));
            connection.On<ReadAppModuleExecutionArtifactRequest, AppModuleExecutionArtifactResponse>(ReadAppModuleExecutionResultMethod, request =>
                Task.FromResult(appActionExecutionService.ReadResult(request)));
            connection.On<AppActionModuleRequest, AppActionModulesResponse>(ListAppActionModulesMethod, request =>
                Task.FromResult(actionDeploymentService.ListModules(
                    request.AppId,
                    request.DeploymentTargetId)));
            connection.On<AppActionModuleRequest, AppActionVersionsResponse>(ListAppActionVersionsMethod, request =>
                Task.FromResult(actionDeploymentService.ListVersions(
                    request.AppId,
                    request.DeploymentTargetId,
                    request.ActionKey)));
            connection.On<AppActionVersionRequest, AppActionVersionOperationResponse>(RestoreAppActionVersionMethod, request =>
                Task.FromResult(actionDeploymentService.RestoreVersion(
                    request.AppId,
                    request.DeploymentTargetId,
                    request.ActionKey,
                    request.Version)));
            connection.On<AppActionVersionRequest, AppActionVersionOperationResponse>(DeleteAppActionVersionMethod, request =>
                Task.FromResult(actionDeploymentService.DeleteVersion(
                    request.AppId,
                    request.DeploymentTargetId,
                    request.ActionKey,
                    request.Version)));
            connection.On<PrepareAppActionModuleRequest, AppActionDeploymentResult>(PrepareAppActionModuleMethod, request =>
                actionDeploymentService.PrepareModuleAsync(request, CancellationToken.None));
            connection.On<SynchronizeAppActionModulesRequest, int>(SynchronizeAppActionModulesMethod, request =>
                Task.FromResult(actionDeploymentService.SynchronizeModules(request)));
            connection.On<CleanupAppDeploymentTargetRequest, CleanupAppDeploymentTargetResult>(
                CleanupAppDeploymentTargetMethod,
                CleanupDeploymentTargetAsync);

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

                    await SendHeartbeatAsync(cancellationToken);
                });
                return Task.CompletedTask;
            };

            await connection.StartAsync(cancellationToken);
            await StartHeartbeatLoopAsync(cancellationToken);
            await SendHeartbeatAsync(cancellationToken);

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
    /// Replaces the managed connected-flow artifact set under the generic application artifact root.
    /// </summary>
    private SynchronizeApplicationManagedArtifactsResult SynchronizeApplicationManagedArtifacts(
        SynchronizeApplicationManagedArtifactsRequest request)
    {
        var artifactRoot = runnerWorkspaceService.GetAppFlowArtifactsRoot(request.AppId, request.DeploymentTargetId);

        try
        {
            ManagedApplicationArtifactSynchronizer.Replace(
                artifactRoot,
                request.Files ?? [],
                runnerWorkspaceService.NormalizeRelativePath);

            return new SynchronizeApplicationManagedArtifactsResult(request.AppId, request.DeploymentTargetId, true);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to synchronize managed flow artifacts for AppId={AppId}", request.AppId);
            return new SynchronizeApplicationManagedArtifactsResult(request.AppId, request.DeploymentTargetId, false, ex.Message);
        }
    }

    private async Task<CleanupAppDeploymentTargetResult> CleanupDeploymentTargetAsync(
        CleanupAppDeploymentTargetRequest request)
    {
        try
        {
            await runnerDeploymentService.CleanupTargetAsync(
                request.AppId,
                request.DeploymentTargetId);

            return new CleanupAppDeploymentTargetResult(
                request.AppId,
                request.DeploymentTargetId,
                Success: true);
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Failed to clean up app {AppId} target {DeploymentTargetId}",
                request.AppId,
                request.DeploymentTargetId);
            return new CleanupAppDeploymentTargetResult(
                request.AppId,
                request.DeploymentTargetId,
                Success: false,
                Error: "Runner runtime cleanup failed.");
        }
    }

    private static Guid ParseAppId(string appId)
    {
        if (!Guid.TryParse(appId, out var parsed))
        {
            throw new ArgumentException("Invalid app ID.");
        }

        return parsed;
    }

    /// <summary>
    /// Returns true when the runner is executing inside a Docker container.
    /// </summary>
    private static bool IsRunningInContainer =>
        // .NET SDK images set this; also set by docker-compose.runner.yml explicitly
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
        // Fallback: Linux containers always have this sentinel file
        || File.Exists("/.dockerenv");

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
        if (retryTask is not null)
            await retryTask;
        retryTask = null;
        retryTokenSource?.Dispose();
        retryTokenSource = null;
        await StopHeartbeatLoopAsync();
        
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

    private async Task<T> ExecuteWithRunnerActivityAsync<T>(Func<Task<T>> operation)
    {
        BeginRunnerActivity();
        try
        {
            return await operation();
        }
        finally
        {
            EndRunnerActivity();
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

        await StopHeartbeatLoopAsync();

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

    private async Task StartHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        await heartbeatLifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await StopHeartbeatLoopCoreAsync();
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            heartbeatTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            heartbeatTask = RunHeartbeatLoopAsync(heartbeatTokenSource.Token);
        }
        finally
        {
            heartbeatLifecycleLock.Release();
        }
    }

    private async Task StopHeartbeatLoopAsync()
    {
        await heartbeatLifecycleLock.WaitAsync(CancellationToken.None);
        try
        {
            await StopHeartbeatLoopCoreAsync();
        }
        finally
        {
            heartbeatLifecycleLock.Release();
        }
    }

    private async Task StopHeartbeatLoopCoreAsync()
    {
        var tokenSource = heartbeatTokenSource;
        var task = heartbeatTask;
        heartbeatTokenSource = null;
        heartbeatTask = null;
        if (tokenSource == null)
        {
            return;
        }

        tokenSource.Cancel();
        try
        {
            if (task != null)
            {
                await task;
            }
        }
        finally
        {
            tokenSource.Dispose();
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SendHeartbeatAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var currentConnection = connection;
        if (currentConnection == null || currentConnection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await currentConnection.SendAsync(HeartbeatMethod, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
            var filePath = string.Equals(chunk.StorageScope, FileTransferStorageScopes.Media, StringComparison.Ordinal)
                ? ResolveAppMediaUploadPath(chunk)
                : ResolveTempUploadPath(chunk);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

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
            string filePath;
            string fileName;
            string contentType;

            if (string.Equals(request.StorageScope, FileTransferStorageScopes.Media, StringComparison.Ordinal))
            {
                if (!request.AppId.HasValue)
                {
                    await SendFileChunkErrorAsync(request.SessionId, 400, "App ID is required.");
                    return;
                }

                filePath = ResolveAppMediaFilePath(request.AppId.Value, request.RelativePath);
                if (!File.Exists(filePath))
                {
                    await SendFileChunkErrorAsync(request.SessionId, 404, "File not found.");
                    return;
                }

                fileName = Path.GetFileName(filePath);
                contentType = GetContentType(fileName);
            }
            else
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

                filePath = files[0];
                fileName = Path.GetFileName(filePath);
                contentType = GetContentType(fileName);
            }

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
            await SendFileChunkErrorAsync(request.SessionId, 500, ex.Message);
        }
    }

    private static string ResolveTempUploadPath(FileUploadChunk chunk)
    {
        var dir = Path.Combine(Path.GetTempPath(), "uploads", chunk.SessionId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, chunk.FileName);
    }

    private string ResolveAppMediaUploadPath(FileUploadChunk chunk)
    {
        if (!chunk.AppId.HasValue)
        {
            throw new ArgumentException("App ID is required for media uploads.");
        }

        return runnerWorkspaceService.ResolveAppMediaFilePath(chunk.AppId.Value, chunk.RelativePath);
    }

    private string GetAppMediaDirectory(Guid appId)
    {
        return Path.GetFullPath(Path.Combine(runnerWorkspaceService.GetLegacyControllerDataDirectory(appId), "media"));
    }

    private string ResolveAppMediaFilePath(Guid appId, string relativePath)
    {
        return runnerWorkspaceService.ResolveAppMediaFilePath(appId, relativePath);
    }

    private AppMediaStatsResponse GetAppMediaStats(string appId)
    {
        try
        {
            var mediaDir = GetAppMediaDirectory(ParseAppId(appId));
            if (!Directory.Exists(mediaDir))
            {
                return new AppMediaStatsResponse { Success = true };
            }

            var files = Directory.EnumerateFiles(mediaDir, "*", SearchOption.AllDirectories).ToList();
            return new AppMediaStatsResponse
            {
                Success = true,
                FileCount = files.Count,
                TotalBytes = files.Sum(path => new FileInfo(path).Length)
            };
        }
        catch (Exception ex)
        {
            return new AppMediaStatsResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private AppMediaFileMetadataResponse GetAppMediaFileMetadata(string appId, string relativePath)
    {
        try
        {
            var filePath = runnerWorkspaceService.ResolveAppMediaFilePath(ParseAppId(appId), relativePath);
            if (!File.Exists(filePath))
            {
                return new AppMediaFileMetadataResponse
                {
                    Success = false,
                    StatusCode = 404,
                    Error = "File not found."
                };
            }

            var info = new FileInfo(filePath);
            return new AppMediaFileMetadataResponse
            {
                Success = true,
                StatusCode = 200,
                FileName = info.Name,
                ContentType = GetContentType(info.Name),
                Length = info.Length
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new AppMediaFileMetadataResponse
            {
                Success = false,
                StatusCode = 403,
                Error = ex.Message
            };
        }
        catch (ArgumentException ex)
        {
            return new AppMediaFileMetadataResponse
            {
                Success = false,
                StatusCode = 400,
                Error = ex.Message
            };
        }
        catch (Exception ex)
        {
            return new AppMediaFileMetadataResponse
            {
                Success = false,
                StatusCode = 500,
                Error = ex.Message
            };
        }
    }

    private async Task SendFileChunkErrorAsync(string sessionId, int statusCode, string error)
    {
        await connection!.SendAsync(FileChunkReadyMethod, new FileChunkReadyMessage
        {
            SessionId = sessionId,
            StatusCode = statusCode,
            Error = error,
            Data = [],
            IsLast = true,
            FileName = "error",
            ContentType = "application/octet-stream"
        });
    }

    private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    private static string GetRunnerReleaseVersion()
        => typeof(HubService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(HubService).Assembly.GetName().Version?.ToString()
            ?? "unknown";

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
