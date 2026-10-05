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

public class HubService : IRuntimeTunnelReconnectRequester
{
    internal const string CurrentRunnerApiCompatibilityVersion = RunnerApiCompatibilityVersions.Current;
    private static readonly TimeSpan TunnelReconnectGracePeriod = TimeSpan.FromSeconds(20);

    private HubConnection? connection;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly ILogger logger;
    private readonly IConnectionPolicy resilientConnectionPolicy;
    private readonly AppActionExecutionService appActionExecutionService;
    private readonly HubExecutionHandlerRegistrar executionHandlerRegistrar;
    private readonly HubControlHandlerRegistrar controlHandlerRegistrar;
    private readonly HubConnectionFailureReporter connectionFailureReporter;
    private readonly SemaphoreSlim connectionLoopSignal = new(0, 1);
    private readonly RunnerTunnelReconnectState tunnelReconnectState = new();
    private int tunnelReconnectGraceStarted;
    private string? connectedApiInstanceId;
    private string? connectedApiColor;
    private bool isReconnecting = false;
    private bool shouldKeepRetrying = true;
    private CancellationTokenSource? retryTokenSource;
    private Task? retryTask;
    private readonly HubHeartbeatService heartbeatService;

    public HubService(
        ConfigurationService configService,
        ApiKeyAuthenticationService apiKeyAuthService,
        TunnelForwarderService tunnelForwarder,
        IServiceProvider serviceProvider,
        AppActionExecutionService appActionExecutionService)
    {
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.logger = Log.ForContext<HubService>();
        heartbeatService = new HubHeartbeatService(() => connection, logger);
        this.appActionExecutionService = appActionExecutionService;
        executionHandlerRegistrar = new HubExecutionHandlerRegistrar(
            tunnelForwarder,
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
            if (tunnelReconnectState.ShouldReconnect)
            {
                await DisconnectForTunnelReconnectAsync();
                return true;
            }

            ResetAfterReconnect(state);
            await WaitForConnectionLoopSignalAsync(cancellationToken);
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
        var delay = ConnectionRetryDelay.Calculate(0, exception, 30, 0, DateTimeOffset.UtcNow);
        logger.Debug("Waiting {RetryDelay} before next connection attempt", delay);
        try
        {
            await Task.Delay(delay, cancellationToken);
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
                Interlocked.Exchange(ref connectedApiInstanceId, null);
                Interlocked.Exchange(ref connectedApiColor, null);
                tunnelReconnectState.SetConnectedApiColor(null);
            }

            connection = HubConnectionFactory.Create(configService, apiKey, hubUrl);

            // Increase server timeout for long-running script execution
            // Default is 30 seconds which is too short for HTTP-based Python scripts
            connection.ServerTimeout = TimeSpan.FromMinutes(5);
            
            // Keep-alive interval to maintain connection during long operations
            connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

            executionHandlerRegistrar.Register(
                connection,
                tunnelReconnectState.TryBeginTunnelRequest,
                CompleteTunnelRequest);
            controlHandlerRegistrar.Register(
                connection,
                SetConnectedApiIdentity,
                WakeConnectionLoop,
                value => isReconnecting = value,
                () => isReconnecting,
                HubConnectionFailureReporter.GetFriendlyErrorMessage,
                () => heartbeatService.SendAsync(cancellationToken));
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
                Interlocked.Exchange(ref connectedApiInstanceId, null);
                Interlocked.Exchange(ref connectedApiColor, null);
                WriteLine("📡 Disconnected from server");
            }
        }
    }

    public bool IsConnected()
    {
        return connection?.State == HubConnectionState.Connected;
    }

    public void RequestRuntimeTunnelReconnect(
        string? targetApiInstanceId,
        string? targetApiColor,
        string? reason)
    {
        var isConnected = IsConnected();
        var alreadyOnTarget = string.IsNullOrWhiteSpace(targetApiColor)
            ? string.Equals(Volatile.Read(ref connectedApiInstanceId), targetApiInstanceId, StringComparison.Ordinal)
            : string.Equals(Volatile.Read(ref connectedApiColor), targetApiColor, StringComparison.Ordinal);
        if (isConnected && alreadyOnTarget)
        {
            tunnelReconnectState.RequestReconnect(targetApiColor);
            logger.Debug("Ignoring duplicate runtime tunnel reconnect request for the API instance the Runner already uses");
            return;
        }

        if (!isConnected)
        {
            Interlocked.Exchange(ref connectedApiInstanceId, null);
            Interlocked.Exchange(ref connectedApiColor, null);
            tunnelReconnectState.SetConnectedApiColor(null);
        }

        var canReconnectNow = tunnelReconnectState.RequestReconnect(targetApiColor);
        logger.Information(
            "Runtime tunnel reconnect requested. ActiveTunnelRequests={ActiveTunnelRequests} TargetApiColor={TargetApiColor} Reason={Reason}",
            tunnelReconnectState.ActiveTunnelRequestCount,
            targetApiColor,
            string.IsNullOrWhiteSpace(reason) ? "API color changed" : reason);

        if (canReconnectNow || !isConnected)
        {
            WakeConnectionLoop();
            return;
        }

        if (Interlocked.CompareExchange(ref tunnelReconnectGraceStarted, 1, 0) == 0)
            _ = ExpireTunnelReconnectGracePeriodAsync(retryTokenSource?.Token ?? CancellationToken.None);
    }

    private void CompleteTunnelRequest()
    {
        if (tunnelReconnectState.CompleteTunnelRequest())
            WakeConnectionLoop();
    }

    private async Task ExpireTunnelReconnectGracePeriodAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TunnelReconnectGracePeriod, cancellationToken);
            if (tunnelReconnectState.ExpireGracePeriod())
            {
                logger.Warning("Runtime tunnel reconnect grace period elapsed; reconnecting the Runner");
                WakeConnectionLoop();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task DisconnectForTunnelReconnectAsync()
    {
        var currentConnection = connection;
        if (currentConnection is null)
            return;

        try
        {
            await heartbeatService.StopAsync();
            await currentConnection.StopAsync();
        }
        catch (Exception exception)
        {
            logger.Debug(exception, "Stopping the old API connection for runtime tunnel reconnect failed");
        }

        try
        {
            await currentConnection.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.Debug(exception, "Disposing the old API connection for runtime tunnel reconnect failed");
        }
        finally
        {
            if (ReferenceEquals(connection, currentConnection))
                connection = null;
            Interlocked.Exchange(ref connectedApiInstanceId, null);
            Interlocked.Exchange(ref connectedApiColor, null);
            tunnelReconnectState.SetConnectedApiColor(null);
            tunnelReconnectState.Reset();
            Interlocked.Exchange(ref tunnelReconnectGraceStarted, 0);
        }
    }

    private Task WaitForConnectionLoopSignalAsync(CancellationToken cancellationToken)
        => connectionLoopSignal.WaitAsync(cancellationToken);

    private void WakeConnectionLoop()
    {
        try
        {
            connectionLoopSignal.Release();
        }
        catch (SemaphoreFullException ex)
        {
            logger.Debug(ex, "A reconnect wake-up is already queued for the connection loop.");
        }
    }

    private void SetConnectedApiIdentity(string? apiInstanceId, string? apiColor)
    {
        if (string.IsNullOrWhiteSpace(apiInstanceId))
            return;

        Interlocked.Exchange(ref connectedApiInstanceId, apiInstanceId);
        Interlocked.Exchange(ref connectedApiColor, apiColor);
        if (tunnelReconnectState.SetConnectedApiColor(apiColor))
            WakeConnectionLoop();
        logger.Debug("Runner connection is registered to API instance {ApiInstanceId}", apiInstanceId);
    }

    private sealed class RetryLoopState
    {
        public bool WasDisconnected { get; set; }
    }
}
