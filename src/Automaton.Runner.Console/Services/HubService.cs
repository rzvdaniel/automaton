using Automaton.Core.Models;
using Automaton.Core.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace Automaton.Runner.Console.Services;

public class HubService
{
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunWorkflowMethod = "RunWorkflow";
    private const string PingMethod = "Ping";

    private HubConnection? connection;
    private readonly ConfigurationService configService;
    private readonly ConsoleAuthenticationService authService;
    private readonly FlowExecutionService flowExecutionService;
    private readonly ILogger logger;

    public HubService(
        ConfigurationService configService,
        ConsoleAuthenticationService authService,
        FlowExecutionService flowExecutionService)
    {
        this.configService = configService;
        this.authService = authService;
        this.flowExecutionService = flowExecutionService;
        this.logger = Log.ForContext<HubService>();
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            var accessToken = await authService.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                System.Console.WriteLine("? Not authenticated. Please login first.");
                return false;
            }

            if (!configService.IsRunnerRegistered())
            {
                System.Console.WriteLine("? Runner not registered. Please register first.");
                return false;
            }

            var hubUrl = $"{configService.BaseUrl}/{configService.WorkflowHubUrl}";
            
            connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(accessToken)!;
                    options.Headers.Add(RunnerIdHeader, configService.RunnerId);
                    options.Headers.Add(RunnerNameHeader, configService.ApplicationName);
                })
                .Build();

            // Register hub methods
            connection.On<Guid, Task>(RunWorkflowMethod, async (workflowId) =>
            {
                try
                {
                    System.Console.WriteLine($"?? Executing workflow: {workflowId}");
                    var result = await flowExecutionService.ExecuteWorkflowAsync(workflowId);
                    System.Console.WriteLine($"? Workflow completed: {result.Status}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error executing workflow {WorkflowId}", workflowId);
                    System.Console.WriteLine($"? Workflow failed: {ex.Message}");
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
                System.Console.WriteLine("? Connection to server lost. Attempting to reconnect...");
                await Task.Delay(5000);
                await ConnectAsync();
            };

            connection.Reconnecting += (error) =>
            {
                logger.Information("Reconnecting to server...");
                System.Console.WriteLine("?? Reconnecting to server...");
                return Task.CompletedTask;
            };

            connection.Reconnected += (connectionId) =>
            {
                logger.Information("Reconnected to server");
                System.Console.WriteLine("? Reconnected to server");
                return Task.CompletedTask;
            };

            System.Console.WriteLine($"?? Connecting to {hubUrl}...");
            await connection.StartAsync();
            
            System.Console.WriteLine($"? Connected as '{configService.ApplicationName}' (ID: {configService.RunnerId})");
            logger.Information("Runner connected to hub successfully");
            
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to connect to hub");
            System.Console.WriteLine($"? Connection failed: {ex.Message}");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (connection != null)
        {
            await connection.DisposeAsync();
            System.Console.WriteLine("?? Disconnected from server");
            logger.Information("Runner disconnected from hub");
        }
    }

    public bool IsConnected => connection?.State == HubConnectionState.Connected;
}