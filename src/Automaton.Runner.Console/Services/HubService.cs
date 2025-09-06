using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
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

    public HubService(
        ConfigurationService configService,
        ApiKeyAuthenticationService apiKeyAuthService,
        FlowExecutionService flowExecutionService)
    {
        this.configService = configService;
        this.apiKeyAuthService = apiKeyAuthService;
        this.flowExecutionService = flowExecutionService;
        this.logger = Log.ForContext<HubService>();
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

            var hubUrl = $"{configService.BaseUrl}/{configService.WorkflowHubUrl}";
            WriteLine($"🔗 Connecting to {hubUrl}...");
            WriteLine($"🔍 API Key: {apiKey[..Math.Min(15, apiKey.Length)]}...");
            WriteLine($"🔍 Runner ID: {configService.RunnerId}");
            WriteLine($"🔍 Runner Name: {configService.ApplicationName}");
            
            connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.Headers.Add("X-API-Key", apiKey);
                    options.Headers.Add(RunnerIdHeader, configService.RunnerId);
                    options.Headers.Add(RunnerNameHeader, configService.ApplicationName);
                })
                .Build();

            // Register hub methods
            connection.On<Guid, Task>(RunWorkflowMethod, async (workflowId) =>
            {
                try
                {
                    WriteLine($"? Executing workflow: {workflowId}");
                    var result = await flowExecutionService.ExecuteWorkflowAsync(workflowId);
                    WriteLine($"? Workflow completed: {result.Status}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error executing workflow {WorkflowId}", workflowId);
                    WriteLine($"? Workflow failed: {ex.Message}");
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
                logger.Warning("Connection closed: {Error}", error?.Message);
                WriteLine("? Connection to server lost. Attempting to reconnect...");
                await Task.Delay(5000);
                await ConnectAsync();
            };

            connection.Reconnecting += (error) =>
            {
                logger.Information("Reconnecting to server: {Error}", error?.Message);
                WriteLine("?? Reconnecting to server...");
                return Task.CompletedTask;
            };

            connection.Reconnected += (connectionId) =>
            {
                logger.Information("Reconnected to server with connection ID: {ConnectionId}", connectionId);
                WriteLine("? Reconnected to server successfully");
                return Task.CompletedTask;
            };

            await connection.StartAsync();

            WriteLine("? Connected to server successfully");
            logger.Information("Connected to hub at {HubUrl}", hubUrl);
            
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to connect to hub");
            WriteLine($"? Connection failed: {ex.Message}");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (connection != null)
        {
            await connection.DisposeAsync();
            connection = null;
            WriteLine("? Disconnected from server");
        }
    }

    public bool IsConnected()
    {
        return connection?.State == HubConnectionState.Connected;
    }
}