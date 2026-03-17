using Automaton.Core.Models;
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
    private readonly TunnelRequestRegistry tunnelRegistry;
    private readonly FileUploadRegistry fileUploadRegistry;
    private readonly FileDownloadRegistry fileDownloadRegistry;

    public AutomatonHub(
        RunnerService runnerService,
        TunnelRequestRegistry tunnelRegistry,
        FileUploadRegistry fileUploadRegistry,
        FileDownloadRegistry fileDownloadRegistry)
    {
        this.runnerService = runnerService;
        this.tunnelRegistry = tunnelRegistry;
        this.fileUploadRegistry = fileUploadRegistry;
        this.fileDownloadRegistry = fileDownloadRegistry;
    }

    public override async Task OnConnectedAsync()
    {
        var runner = GetRunner();

        await Clients.Caller.SendAsync("WelcomeRunner", $"Welcome {runner.Name}");

        await UpdateRunnerConnection();

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called by the runner to deliver a tunnel response back to the server.
    /// Resolves the pending TaskCompletionSource by requestId.
    /// </summary>
    public Task TunnelResponse(TunnelResponse response)
    {
        tunnelRegistry.TryComplete(response);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the runner when all upload chunks have been written to its local filesystem.
    /// </summary>
    public Task FileUploadComplete(FileUploadCompleteMessage message)
    {
        fileUploadRegistry.TryComplete(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the runner to deliver a chunk of a requested download file.
    /// </summary>
    public Task FileChunkReady(FileChunkReadyMessage message)
    {
        fileDownloadRegistry.TryWrite(message);
        if (message.IsLast)
            fileDownloadRegistry.Complete(message.SessionId);
        return Task.CompletedTask;
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
