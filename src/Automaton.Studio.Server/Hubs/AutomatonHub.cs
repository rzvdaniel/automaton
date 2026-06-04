using Automaton.Core.Models;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Automaton.Studio.Server.Hubs;

[Authorize(Policy = "AllowApiKey")] // Use AllowApiKey policy to support console runner API key authentication
public class AutomatonHub : Hub
{
    private const string UserIdClaim = "uid";
    private const string RunnerIdHeader = "RunnerId";
    private const string RunnerNameHeader = "RunnerName";
    private const string RunnerVersionHeader = "RunnerVersion";
    private const string RunnerMachineNameHeader = "RunnerMachineName";

    private readonly RunnerService runnerService;
    private readonly ApiInstanceIdentityService apiInstanceIdentityService;
    private readonly TunnelRequestRegistry tunnelRegistry;
    private readonly FileUploadRegistry fileUploadRegistry;
    private readonly FileDownloadRegistry fileDownloadRegistry;
    private readonly ILogger<AutomatonHub> logger;

    public AutomatonHub(
        RunnerService runnerService,
        ApiInstanceIdentityService apiInstanceIdentityService,
        TunnelRequestRegistry tunnelRegistry,
        FileUploadRegistry fileUploadRegistry,
        FileDownloadRegistry fileDownloadRegistry,
        ILogger<AutomatonHub> logger)
    {
        this.runnerService = runnerService;
        this.apiInstanceIdentityService = apiInstanceIdentityService;
        this.tunnelRegistry = tunnelRegistry;
        this.fileUploadRegistry = fileUploadRegistry;
        this.fileDownloadRegistry = fileDownloadRegistry;
        this.logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var runner = GetRunner();

        try
        {
            await UpdateRunnerConnection();
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Rejected runner connection for runner {RunnerId} ({RunnerName}) due to authorization mismatch", runner.Id, runner.Name);
            Context.Abort();
            return;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Rejected runner connection for runner {RunnerId} ({RunnerName}) because the runner record is invalid or missing", runner.Id, runner.Name);
            Context.Abort();
            return;
        }

        logger.LogInformation(
            "Runner connected successfully: runner {RunnerId} ({RunnerName}) on connection {ConnectionId} via API instance {ApiInstanceId} color {ApiColor}",
            runner.Id,
            runner.Name,
            Context.ConnectionId,
            apiInstanceIdentityService.ApiInstanceId,
            apiInstanceIdentityService.ApiColor ?? "(unset)");

        await Clients.Caller.SendAsync("WelcomeRunner", new WelcomeRunnerMessage
        {
            Message = $"Welcome {runner.Name}",
            ApiInstanceId = apiInstanceIdentityService.ApiInstanceId,
            ApiColor = apiInstanceIdentityService.ApiColor
        });

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
    /// Called by the runner when a chunk could not be written (disk error, permissions, etc.).
    /// </summary>
    public Task FileUploadFailed(FileUploadFailedMessage message)
    {
        fileUploadRegistry.TryFail(message);
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
        try
        {
            var runner = GetRunner();
            await runnerService.MarkDisconnectedAsync(runner.Id, Context.ConnectionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update runner disconnect state for connection {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public bool Ping(string runnerName)
    {
        return !string.IsNullOrEmpty(runnerName);
    }

    public async Task Heartbeat()
    {
        var runner = GetRunner();
        var userId = GetUserId();

        try
        {
            await runnerService.RecordHeartbeatAsync(runner.Id, userId, runner, CancellationToken.None);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Rejected heartbeat for runner {RunnerId} ({RunnerName}) due to authorization mismatch", runner.Id, runner.Name);
            Context.Abort();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Rejected heartbeat for runner {RunnerId} ({RunnerName}) because the runner record is invalid or missing", runner.Id, runner.Name);
            Context.Abort();
        }
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
            ConnectionId = connectionId,
            Version = httpContext.Request.Headers[RunnerVersionHeader].ToString(),
            MachineName = httpContext.Request.Headers[RunnerMachineNameHeader].ToString(),
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            ApiInstanceId = apiInstanceIdentityService.ApiInstanceId,
            ApiColor = apiInstanceIdentityService.ApiColor,
        };

        return runner;
    }

    private Guid GetUserId()
    {
        var userId = Context.User.Claims
            .Where(x => x.Type == UserIdClaim || x.Type == ClaimTypes.NameIdentifier)
            .Select(x => Guid.TryParse(x.Value, out var parsedUserId) ? parsedUserId : Guid.Empty)
            .FirstOrDefault(x => x != Guid.Empty);

        if (userId == Guid.Empty)
        {
            throw new InvalidOperationException("Authenticated runner connection is missing a valid user id claim.");
        }

        return userId;
    }

    private async Task UpdateRunnerConnection()
    {
        var runner = GetRunner();
        var userId = GetUserId();

        await runnerService.UpdateConnection(runner.Id, userId, runner, CancellationToken.None);
    }
}
