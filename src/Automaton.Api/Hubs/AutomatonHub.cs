using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Automaton.Api.Hubs;

[Authorize(Policy = "AllowApiKey")] // Use AllowApiKey policy to support console runner API key authentication
public class AutomatonHub : Hub
{
    private const string UserIdClaim = "uid";
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

    private readonly RunnerService runnerService;
    private readonly AppsService appsService;
    private readonly ApiInstanceIdentityService apiInstanceIdentityService;
    private readonly TunnelRequestRegistry tunnelRegistry;
    private readonly FileUploadRegistry fileUploadRegistry;
    private readonly FileDownloadRegistry fileDownloadRegistry;
    private readonly ILogger<AutomatonHub> logger;
    private readonly RunnerStorageTelemetryService storageTelemetryService;
    private readonly IAppDeploymentTargetResolver deploymentTargetResolver;
    private readonly HostContainerRelayService hostContainerRelayService;

    public AutomatonHub(
        RunnerService runnerService,
        AppsService appsService,
        ApiInstanceIdentityService apiInstanceIdentityService,
        TunnelRequestRegistry tunnelRegistry,
        FileUploadRegistry fileUploadRegistry,
        FileDownloadRegistry fileDownloadRegistry,
        ILogger<AutomatonHub> logger,
        RunnerStorageTelemetryService storageTelemetryService,
        IAppDeploymentTargetResolver deploymentTargetResolver,
        HostContainerRelayService hostContainerRelayService)
    {
        this.runnerService = runnerService;
        this.appsService = appsService;
        this.apiInstanceIdentityService = apiInstanceIdentityService;
        this.tunnelRegistry = tunnelRegistry;
        this.fileUploadRegistry = fileUploadRegistry;
        this.fileDownloadRegistry = fileDownloadRegistry;
        this.logger = logger;
        this.storageTelemetryService = storageTelemetryService;
        this.deploymentTargetResolver = deploymentTargetResolver;
        this.hostContainerRelayService = hostContainerRelayService;
    }

    public Task ReportHostedRunnerStorage(HostedRunnerStorageTelemetry report) =>
        storageTelemetryService.ReportHostedRunnerAsync(GetRunner(), report, Context.ConnectionAborted);

    public Task ReportRunnerHostStorage(RunnerHostStorageTelemetry report) =>
        storageTelemetryService.ReportRunnerHostAsync(GetRunner(), report, Context.ConnectionAborted);

    public Task<HostContainerExecutionResponse> RequestHostContainerExecution(HostContainerExecutionRequest request) =>
        hostContainerRelayService.ExecuteAsync(GetRunner(), request, Context.ConnectionAborted);

    public override async Task OnConnectedAsync()
    {
        var runner = GetRunnerOrNull();
        if (runner == null)
        {
            await base.OnConnectedAsync();
            return;
        }

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
        var runner = GetRunnerOrNull();
        if (runner == null)
        {
            await base.OnDisconnectedAsync(exception);
            return;
        }

        try
        {
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

    public async Task JoinAppRuntimeLogs(Guid appId, Guid deploymentTargetId)
    {
        if (GetRunnerOrNull() != null)
        {
            throw new HubException("Runner connections cannot subscribe to app runtime logs.");
        }

        var userId = GetUserId();
        var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, CancellationToken.None);
        if (app == null)
        {
            throw new HubException("App not found or access denied.");
        }

        var target = await deploymentTargetResolver.ResolveByIdAsync(
            appId,
            deploymentTargetId,
            userId,
            Context.ConnectionAborted);
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            GetAppRuntimeLogsGroupName(appId, target.Id));
    }

    public async Task LeaveAppRuntimeLogs(Guid appId, Guid deploymentTargetId)
    {
        var target = await deploymentTargetResolver.ResolveByIdAsync(
            appId,
            deploymentTargetId,
            GetUserId(),
            Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            GetAppRuntimeLogsGroupName(appId, target.Id));
    }

    public static string GetAppRuntimeLogsGroupName(
        Guid appId,
        Guid deploymentTargetId)
        => $"app-runtime-logs:{appId:D}:{deploymentTargetId:D}";

    public Task ReconnectWhenIdleAcknowledged(string? reason)
    {
        var runner = GetRunner();
        logger.LogInformation(
            "Runner acknowledged reconnect-when-idle request: RunnerId={RunnerId} RunnerName={RunnerName} ConnectionId={ConnectionId} Reason={Reason}",
            runner.Id,
            runner.Name,
            Context.ConnectionId,
            reason);
        return Task.CompletedTask;
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

    private RunnerDetails GetRunner()
    {
        var runner = GetRunnerOrNull();
        if (runner == null)
        {
            throw new InvalidOperationException("SignalR connection does not include runner headers.");
        }

        return runner;
    }

    private RunnerDetails? GetRunnerOrNull()
    {
        var httpContext = Context.GetHttpContext();
        var runnerId = httpContext.Request.Headers[RunnerIdHeader].ToString();
        var runnerName = httpContext.Request.Headers[RunnerNameHeader].ToString();
        var connectionId = Context.ConnectionId;

        if (string.IsNullOrWhiteSpace(runnerId) || !Guid.TryParse(runnerId, out var parsedRunnerId))
        {
            return null;
        }

        return new Models.RunnerDetails
        {
            Id = parsedRunnerId,
            Name = runnerName,
            ConnectionId = connectionId,
            Version = httpContext.Request.Headers[RunnerVersionHeader].ToString(),
            MachineName = httpContext.Request.Headers[RunnerMachineNameHeader].ToString(),
            Purpose = RunnerPurposes.Normalize(httpContext.Request.Headers[RunnerPurposeHeader].ToString()),
            Capabilities = RunnerCapabilities.Normalize(
                httpContext.Request.Headers[RunnerCapabilitiesHeader]
                    .ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                httpContext.Request.Headers[RunnerPurposeHeader].ToString()),
            RunnerHostId = Guid.TryParse(httpContext.Request.Headers[RunnerHostIdHeader].ToString(), out var hostId) ? hostId : null,
            HostedRunnerId = Guid.TryParse(httpContext.Request.Headers[HostedRunnerIdHeader].ToString(), out var hostedRunnerId)
                ? hostedRunnerId
                : null,
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            ApiInstanceId = apiInstanceIdentityService.ApiInstanceId,
            ApiColor = apiInstanceIdentityService.ApiColor,
            ImageDigest = httpContext.Request.Headers[RunnerImageDigestHeader].ToString(),
            ContainerSpecificationVersion = int.TryParse(httpContext.Request.Headers[RunnerSpecificationVersionHeader].ToString(), out var specificationVersion) ? specificationVersion : null,
            ApiCompatibilityVersion = httpContext.Request.Headers[RunnerApiCompatibilityVersionHeader].ToString(),
            OperatingSystem = httpContext.Request.Headers[RunnerOperatingSystemHeader].ToString(),
            Architecture = httpContext.Request.Headers[RunnerArchitectureHeader].ToString(),
            ContainerHealth = httpContext.Request.Headers[RunnerContainerHealthHeader].ToString(),
        };
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
