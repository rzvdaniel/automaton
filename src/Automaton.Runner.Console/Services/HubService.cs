using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using System.Net.Http;
using System.Net.Sockets;
using Automaton.Runner.Console.Connection;
using static System.Console;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Runner.Console.Services;

public class HubService
{
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunWorkflowMethod = "RunWorkflow";
    private const string RunCustomStepMethod = "RunCustomStep";
    private const string PingMethod = "Ping";

    private HubConnection? connection;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly FlowExecutionService flowExecutionService;
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
        IServiceProvider serviceProvider)
    {
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.flowExecutionService = flowExecutionService;
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
                await BuildAndConnectWithRetry(apiKey, hubUrl);
                
                // If we get here, connection succeeded
                logger.Information("Successfully connected via continuous retry loop");
                WriteLine("✅ Connection established successfully after retries!");
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

    private async Task BuildAndConnectWithRetry(string apiKey, string hubUrl)
    {
        await resilientConnectionPolicy.GetPolicy().ExecuteAsync(async () =>
        {
            await BuildAndConnect(apiKey, hubUrl);
        });
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
                })
                .WithAutomaticReconnect(new[] 
                { 
                    TimeSpan.FromSeconds(2), 
                    TimeSpan.FromSeconds(5), 
                    TimeSpan.FromSeconds(10), 
                    TimeSpan.FromSeconds(30) 
                })
                .Build();

            // Register hub methods
            connection.On<Guid, Task>(RunWorkflowMethod, async (workflowId) =>
            {
                try
                {
                    WriteLine($"▶️ Executing workflow: {workflowId}");
                    var result = await flowExecutionService.ExecuteWorkflowAsync(workflowId);
                    WriteLine($"✅ Workflow completed: {result.Status}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error executing workflow {WorkflowId}", workflowId);
                    WriteLine($"❌ Workflow failed: {ex.Message}");
                }
            });

            // Register custom step execution handler that returns CustomStepExecutionResponse
            // Using Func<> to support return values with SignalR
            connection.On<CustomStepPayload, CustomStepExecutionResponse>(RunCustomStepMethod, (customStep) =>
            {
                try
                {
                    // Create a scope and resolve CustomStepHandler for this request
                    using var scope = serviceProvider.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<CustomStepExecuteService>();
                    
                    // Execute and wait for result (SignalR requires synchronous return)
                    var result = handler.RunCustomStep(customStep).GetAwaiter().GetResult();
                    
                    logger.Information("Custom step {CustomStepId} completed with status: {Success}", 
                        customStep.Id, result.Success ? "Success" : "Failed");
                    
                    return result;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error executing custom step {CustomStepId}", customStep.Id);
                    WriteLine($"❌ Custom step failed: {ex.Message}");
                    
                    // Return error result
                    return new CustomStepExecutionResponse
                    {
                        Success = false,
                        Output = null,
                        Error = $"Handler exception: {ex.Message}",
                        Started = DateTime.UtcNow,
                        Finished = DateTime.UtcNow,
                        OutputVariables = new Dictionary<string, object>()
                    };
                }
            });

            connection.On(PingMethod, () =>
            {
                logger.Debug("Received ping from server");
                return "Pong";
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

            WriteLine("✅ Connected to server successfully");
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
}