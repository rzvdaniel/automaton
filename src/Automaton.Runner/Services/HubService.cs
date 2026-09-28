using Microsoft.AspNetCore.SignalR.Client;
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
    private const string ReportHostedRunnerStorageMethod = "ReportHostedRunnerStorage";
    private const string ReportRunnerHostStorageMethod = "ReportRunnerHostStorage";
    private const string RequestHostContainerExecutionMethod = "RequestHostContainerExecution";

    private HubConnection? connection;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly ILogger logger;
    private readonly IConnectionPolicy resilientConnectionPolicy;
    private readonly HubFileTransferService fileTransferService;
    private readonly RunnerDeploymentService runnerDeploymentService;
    private readonly AppActionExecutionService appActionExecutionService;
    private readonly HubAppActionHandlerRegistrar appActionHandlerRegistrar;
    private readonly HubExecutionHandlerRegistrar executionHandlerRegistrar;
    private readonly HubControlHandlerRegistrar controlHandlerRegistrar;
    private readonly HubConnectionFailureReporter connectionFailureReporter;
    private readonly SemaphoreSlim reconnectLoopSignal = new(0, 1);
    private bool isReconnecting = false;
    private bool shouldKeepRetrying = true;
    private CancellationTokenSource? retryTokenSource;
    private Task? retryTask;
    private int activeOperationCount = 0;
    private int reconnectRequested = 0;
    private readonly HubHeartbeatService heartbeatService;

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
        this.logger = Log.ForContext<HubService>();
        heartbeatService = new HubHeartbeatService(() => connection, logger);
        fileTransferService = new HubFileTransferService(runnerWorkspaceService, logger);
        this.runnerDeploymentService = runnerDeploymentService;
        this.appActionExecutionService = appActionExecutionService;
        appActionHandlerRegistrar = new HubAppActionHandlerRegistrar(
            runnerDeploymentService,
            appActionExecutionService,
            actionDeploymentService);
        executionHandlerRegistrar = new HubExecutionHandlerRegistrar(
            configService,
            flowExecutionService,
            tunnelForwarder,
            serviceProvider,
            runnerWorkspaceService,
            infrastructureCommandService,
            hostContainerExecutionService,
            logger);
        controlHandlerRegistrar = new HubControlHandlerRegistrar(serviceProvider, logger);
        connectionFailureReporter = new HubConnectionFailureReporter(configService, logger);

        // Configure resilient connection policy
        this.resilientConnectionPolicy = new ResilientPolicy(
            retryAfterSeconds: 5,
            exceptionsAllowedBeforeBreaking: 3,
            durationOfBreak: TimeSpan.FromMinutes(2)
        );
    }

    public async Task<bool> ConnectAsync()
    {
        RunnerTemporaryFileCleanup.RemoveStaleUploadAndResultDirectories();
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
        var state = new RetryLoopState();
        while (shouldKeepRetrying && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await RunRetryCycleAsync(state, apiKey, hubUrl, cancellationToken)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!await WaitAfterConnectionFailureAsync(ex, cancellationToken)) break;
            }
        }
        logger.Debug("Continuous retry loop terminated");
    }

    private async Task<bool> RunRetryCycleAsync(
        RetryLoopState state,
        string apiKey,
        string hubUrl,
        CancellationToken cancellationToken)
    {
        if (resilientConnectionPolicy.ShouldStopRetrying())
        {
            WriteLine("⏹️  Maximum retry duration exceeded - stopping connection attempts");
            logger.Warning("Stopping continuous retry loop - maximum retry duration exceeded");
            return false;
        }
        if (IsConnected())
        {
            ResetAfterReconnect(state);
            if (await WaitForReconnectRequestAsync(TimeSpan.FromSeconds(10), cancellationToken))
                await DisconnectConnectionForReconnectAsync();
            return true;
        }

        if (connection?.State == HubConnectionState.Reconnecting)
        {
            await WaitForConnectionLoopSignalAsync(cancellationToken);
            return true;
        }

        state.WasDisconnected = true;
        var attempts = await BuildAndConnectWithRetry(apiKey, hubUrl, cancellationToken);
        ReportConnectionEstablished(attempts);
        state.WasDisconnected = false;
        isReconnecting = false;
        return true;
    }

    private void ResetAfterReconnect(RetryLoopState state)
    {
        if (!state.WasDisconnected) return;
        state.WasDisconnected = false;
        isReconnecting = false;
    }

    private void ReportConnectionEstablished(int attempts)
    {
        logger.Debug("Successfully connected via continuous retry loop (attempts: {Attempts})", attempts);
        if (attempts <= 1)
        {
            WriteLine("✅ Connected to server successfully");
            return;
        }
        WriteLine($"✅ Connection established successfully after {attempts} attempt{(attempts > 1 ? "s" : "")}!");
    }

    private async Task<bool> WaitAfterConnectionFailureAsync(Exception exception, CancellationToken cancellationToken)
    {
        connectionFailureReporter.Report(exception, isReconnecting, () => isReconnecting = true);
        const int delaySeconds = 30;
        logger.Debug("Waiting {DelaySeconds} seconds before next connection attempt", delaySeconds);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
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
                await heartbeatService.StopAsync();
                await connection.DisposeAsync();
                connection = null;
            }

            connection = HubConnectionFactory.Create(configService, apiKey, hubUrl);

            // Increase server timeout for long-running script execution
            // Default is 30 seconds which is too short for HTTP-based Python scripts
            connection.ServerTimeout = TimeSpan.FromMinutes(5);
            
            // Keep-alive interval to maintain connection during long operations
            connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

            executionHandlerRegistrar.Register(connection, BeginRunnerActivity, EndRunnerActivity);
            controlHandlerRegistrar.Register(
                connection,
                HandleReconnectWhenIdle,
                WakeConnectionLoop,
                value => isReconnecting = value,
                () => isReconnecting,
                HubConnectionFailureReporter.GetFriendlyErrorMessage,
                () => heartbeatService.SendAsync(cancellationToken));
            RegisterActionHandlers();

            await connection.StartAsync(cancellationToken);
            await heartbeatService.StartAsync(cancellationToken);
            await heartbeatService.SendAsync(cancellationToken);

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

    private void RegisterActionHandlers()
    {
        var current = connection ?? throw new InvalidOperationException("Hub connection is unavailable.");
        appActionHandlerRegistrar.Register(current, ExecuteWithRunnerActivityAsync, CleanupDeploymentTargetAsync);
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
        await heartbeatService.StopAsync();
        
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
            WakeConnectionLoop();
        }
    }

    internal async Task<T> ExecuteWithRunnerActivityAsync<T>(Func<Task<T>> operation)
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
            Interlocked.Exchange(ref reconnectRequested, 1);
            WakeConnectionLoop();
            return;
        }

        Interlocked.Exchange(ref reconnectRequested, 1);
        WriteLine($"⏳ Reconnect requested by server. Waiting for {activeOperations} active task(s) to finish.");
    }

    private async Task<bool> WaitForReconnectRequestAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        var signaled = await reconnectLoopSignal.WaitAsync(interval, cancellationToken);
        if (!signaled || Volatile.Read(ref reconnectRequested) != 1)
            return false;

        if (Volatile.Read(ref activeOperationCount) != 0)
        {
            return false;
        }

        Interlocked.Exchange(ref reconnectRequested, 0);
        return true;
    }

    private Task WaitForConnectionLoopSignalAsync(CancellationToken cancellationToken)
        => reconnectLoopSignal.WaitAsync(cancellationToken);

    private void WakeConnectionLoop()
    {
        try
        {
            reconnectLoopSignal.Release();
        }
        catch (SemaphoreFullException ex)
        {
            logger.Debug(ex, "A reconnect wake-up is already queued for the connection loop.");
        }
    }

    private async Task DisconnectConnectionForReconnectAsync()
    {
        var currentConnection = connection;
        if (currentConnection == null)
        {
            return;
        }

        try
        {
            try
            {
                await heartbeatService.StopAsync();
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Stopping the heartbeat before reconnect failed");
            }

            try
            {
                await currentConnection.StopAsync();
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Stopping current SignalR connection during reconnect failed");
            }

            try
            {
                await currentConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Disposing current SignalR connection during reconnect failed");
            }
        }
        finally
        {
            if (ReferenceEquals(connection, currentConnection))
                connection = null;
        }
    }

    private sealed class RetryLoopState
    {
        public bool WasDisconnected { get; set; }
    }
}
