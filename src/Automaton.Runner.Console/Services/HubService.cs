using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using System.Net.Http;
using System.Net.Sockets;
using Automaton.Runner.Console.Connection;
using static System.Console;

namespace Automaton.Runner.Console.Services;

public class HubService
{
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunWorkflowMethod = "RunWorkflow";
    private const string PingMethod = "Ping";

    private HubConnection? connection;
    private readonly ConfigurationService configService;
    private readonly ApiKeyAuthenticationService apiKeyAuthService;
    private readonly FlowExecutionService flowExecutionService;
    private readonly ILogger logger;
    private readonly IConnectionPolicy resilientConnectionPolicy;
    private readonly string hubUrl;
    private bool isReconnecting = false;

    public HubService(
        ConfigurationService configService,
        ApiKeyAuthenticationService apiKeyAuthService,
        FlowExecutionService flowExecutionService)
    {
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.flowExecutionService = flowExecutionService;
        this.logger = Log.ForContext<HubService>();
        this.hubUrl = $"{configService.BaseUrl}/{configService.WorkflowHubUrl}";
        
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

            WriteLine($"🔗 Connecting to {hubUrl}...");
            WriteLine($"🔍 API Key: {apiKey[..Math.Min(15, apiKey.Length)]}...");
            WriteLine($"🔍 Runner ID: {configService.RunnerId}");
            WriteLine($"🔍 Runner Name: {configService.ApplicationName}");
            
            await BuildAndConnectWithRetry(apiKey);
            return true;
        }
        catch (Exception ex)
        {
            await HandleConnectionError(ex);
            return false;
        }
    }

    private async Task BuildAndConnectWithRetry(string apiKey)
    {
        await resilientConnectionPolicy.GetPolicy().ExecuteAsync(async () =>
        {
            await BuildAndConnect(apiKey);
        });
    }

    private async Task BuildAndConnect(string apiKey)
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
                    logger.Warning("Connection closed with error: {Error}", error.Message);
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
                var errorMsg = error?.Message ?? "Unknown error";
                logger.Information("Reconnecting to server: {Error}", errorMsg);
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
            logger.Error(ex, "Failed to connect to hub");
            throw; // Re-throw to let the retry policy handle it
        }
    }

    private async Task HandleConnectionError(Exception ex)
    {
        logger.Error(ex, "Failed to connect to hub");
        
        var friendlyMessage = GetFriendlyErrorMessage(ex);
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