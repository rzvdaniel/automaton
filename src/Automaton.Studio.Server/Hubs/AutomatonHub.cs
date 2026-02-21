using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Studio.Server.Hubs;

[Authorize(Policy = "AllowApiKey")] // Use AllowApiKey policy to support console runner API key authentication
public class AutomatonHub : Hub
{
    private const string UserIdClaim = "uid";
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";

    private readonly RunnerService runnerService;
    private readonly AppsControllerExecutionService controllerExecutionService;

    public AutomatonHub(RunnerService runnerService, AppsControllerExecutionService controllerExecutionService)
    {
        this.runnerService = runnerService;
        this.controllerExecutionService = controllerExecutionService;
    }

    public override async Task OnConnectedAsync()
    {
        var runner = GetRunner();

        await Clients.Caller.SendAsync("WelcomeRunner", $"Welcome {runner.Name}");

        await UpdateRunnerConnection();

        // Auto-sync backend scripts for all apps configured to use this runner
        _ = Task.Run(() => controllerExecutionService.SyncScriptsToRunnerAsync(
            runner.Id, runner.ConnectionId, CancellationToken.None));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    public bool Ping(string runnerName)
    {
        return !string.IsNullOrEmpty(runnerName);
    }

    private Models.RunnerDetails GetRunner()
    {
        var httpContext = Context.GetHttpContext();
        var runnerId = httpContext.Request.Headers[RunnerIdHeader].ToString();
        var runnerName = httpContext.Request.Headers[RunnerNameHeader].ToString();
        var connectionId = Context.ConnectionId;

        var runner = new Models.RunnerDetails
        { 
            Id = Guid.Parse(runnerId),
            Name = runnerName,
            ConnectionId = connectionId
        };

        return runner;
    }

    private string GetUserId()
    {
        var userIdClaim = Context.User.Claims.SingleOrDefault(x => x.Type == UserIdClaim);

        if (userIdClaim is null)
            throw new ArgumentNullException("userId");

        return userIdClaim.Value;
    }

    private async Task UpdateRunnerConnection()
    {
        var runner = GetRunner();

        await runnerService.UpdateConnection(runner.Id, runner.ConnectionId, CancellationToken.None);
    }
}
