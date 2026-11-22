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
    private readonly ILogger<AutomatonHub> logger;
    private readonly IAppDeploymentTargetResolver deploymentTargetResolver;
    private readonly RunnerExecutionIdentityService runnerIdentity;

    public AutomatonHub(
        RunnerService runnerService,
        AppsService appsService,
        ApiInstanceIdentityService apiInstanceIdentityService,
        ILogger<AutomatonHub> logger,
        IAppDeploymentTargetResolver deploymentTargetResolver,
        RunnerExecutionIdentityService runnerIdentity)
    {
        this.runnerService = runnerService;
        this.appsService = appsService;
        this.apiInstanceIdentityService = apiInstanceIdentityService;
        this.logger = logger;
        this.deploymentTargetResolver = deploymentTargetResolver;
        this.runnerIdentity = runnerIdentity;
    }

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
            await runnerIdentity.EnsureAsync(GetApiKeyId(), runner.Id, GetUserId(), Context.ConnectionAborted);
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

        await Clients.Caller.SendAsync(
            "ApiInstanceIdentity",
            apiInstanceIdentityService.ApiInstanceId,
            apiInstanceIdentityService.ApiColor,
            Context.ConnectionAborted);

        await base.OnConnectedAsync();
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

    /// <summary>
    /// Runner → API: where the reverse proxy can reach this Runner's runtime gateway, and the secret it must send (#1181).
    /// The address is probed first; a Runner that is not reachable keeps being served through the tunnel.
    /// </summary>
    public async Task ReportRuntimeGateway(string url, string secret)
    {
        var runner = GetRunner();
        var services = Context.GetHttpContext()!.RequestServices;
        try { await runnerIdentity.EnsureAsync(GetApiKeyId(), runner.Id, GetUserId(), Context.ConnectionAborted); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Rejected runtime gateway report for runner {RunnerId}", runner.Id);
            Context.Abort();
            return;
        }

        var registry = services.GetRequiredService<RunnerGatewayRegistry>();
        if (await registry.ReportAsync(runner.Id, url, secret, Context.ConnectionAborted))
        {
            logger.LogInformation("Runner {RunnerId} reported a reachable runtime gateway; refreshing app routes", runner.Id);
            await services.GetRequiredService<BackendRouteConfigService>().RefreshAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Runner → API, with each heartbeat: the app runtimes this Runner hosts. Returns the approved app that receives the
    /// server's logs (<c>logs:receive</c>) when this Runner hosts it, so the Runner can send its own logs there too.
    /// </summary>
    public async Task<LogSinkAssignment?> SyncLogSink(LogSinkRuntimeReport[]? runtimes)
    {
        var runner = GetRunner();
        var services = Context.GetHttpContext()!.RequestServices;
        try { await runnerIdentity.EnsureAsync(GetApiKeyId(), runner.Id, GetUserId(), Context.ConnectionAborted); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Rejected log sink sync for runner {RunnerId}", runner.Id);
            Context.Abort();
            return null;
        }

        // Only a Runner whose gateway the server probed has a verified host; any other Runner receives nothing.
        var host = services.GetRequiredService<RunnerGatewayRegistry>().Find(runner.Id)?.Url.Host;
        return await services.GetRequiredService<LogSinkService>().SyncAsync(runner.Id, host, runtimes, Context.ConnectionAborted);
    }

    /// <summary>Runner → API: runtimes it could not start (and ones that came back), so Studio shows them down instead of live.</summary>
    public async Task ReportRuntimeStates(RuntimeStateReport[]? reports)
    {
        var runner = GetRunner();
        var services = Context.GetHttpContext()!.RequestServices;
        try { await runnerIdentity.EnsureAsync(GetApiKeyId(), runner.Id, GetUserId(), Context.ConnectionAborted); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Rejected runtime state report for runner {RunnerId}", runner.Id);
            Context.Abort();
            return;
        }
        await services.GetRequiredService<AppDeploymentTargetStateService>()
            .ApplyRuntimeReportsAsync(runner.Id, reports, Context.ConnectionAborted);
    }

    public async Task Heartbeat()
    {
        var runner = GetRunner();
        var userId = GetUserId();

        try
        {
            await runnerIdentity.EnsureAsync(GetApiKeyId(), runner.Id, userId, Context.ConnectionAborted);
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
        if (httpContext is null)
        {
            return null;
        }

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
        var userId = Context.User?.Claims
            .Where(x => x.Type == UserIdClaim || x.Type == ClaimTypes.NameIdentifier)
            .Select(x => Guid.TryParse(x.Value, out var parsedUserId) ? parsedUserId : Guid.Empty)
            .FirstOrDefault(x => x != Guid.Empty)
            ?? Guid.Empty;

        if (userId == Guid.Empty)
        {
            throw new InvalidOperationException("Authenticated runner connection is missing a valid user id claim.");
        }

        return userId;
    }

    private Guid GetApiKeyId()
        => Guid.TryParse(Context.User?.FindFirst("api_key_id")?.Value, out var apiKeyId)
            ? apiKeyId
            : Guid.Empty;

    private async Task UpdateRunnerConnection()
    {
        var runner = GetRunner();
        var userId = GetUserId();

        await runnerService.UpdateConnection(runner.Id, userId, runner, CancellationToken.None);
    }
}
