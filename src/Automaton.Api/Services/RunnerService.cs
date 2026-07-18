using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Core.Scripting.Engines;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Automaton.Api.Models;
using Automaton.Api.Enums;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Entities;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Hubs;
using Automaton.Api.Mapping;

namespace Automaton.Api.Services;

public class RunnerService
{
    private readonly ApplicationDbContext dbContext;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly UserContextService userContextService;  // ✅ Store service, not userId
    private readonly ResourceAuthorizationService authorizationService;
    private readonly ApiInstanceIdentityService apiInstanceIdentityService;
    private readonly IAuditEventService auditEventService;
    private readonly IApplicationEventService applicationEventService;
    private readonly ManagedRunnerAppAssignmentService managedRunnerAppAssignmentService;
    private readonly Serilog.ILogger logger;

    // ✅ Helper property to get current userId (respects impersonation)
    private Guid UserId => userContextService.GetUserId();

    public RunnerService(ApplicationDbContext dbContext,
        UserContextService userContextService,
        IHubContext<AutomatonHub> automatonHub,
        ResourceAuthorizationService authorizationService,
        ApiInstanceIdentityService apiInstanceIdentityService,
        IAuditEventService auditEventService,
        IApplicationEventService applicationEventService,
        ManagedRunnerAppAssignmentService managedRunnerAppAssignmentService)
    {
        this.dbContext = dbContext;
        this.automatonHub = automatonHub;
        this.userContextService = userContextService;  // ✅ Store service reference
        this.authorizationService = authorizationService;
        this.apiInstanceIdentityService = apiInstanceIdentityService;
        this.auditEventService = auditEventService;
        this.applicationEventService = applicationEventService;
        this.managedRunnerAppAssignmentService = managedRunnerAppAssignmentService;
        logger = Serilog.Log.ForContext<RunnerService>();
    }

    public async Task<IEnumerable<RunnerDetails>> List(CancellationToken cancellationToken)
    {
        return await List(null, cancellationToken);
    }

    public async Task<IEnumerable<RunnerDetails>> List(Guid? projectId, CancellationToken cancellationToken)
    {
        // Use authorization service to get runners filtered by project context
        var runnerQuery = authorizationService.GetRunnersQueryByContext(projectId, UserId);
        var runnerEntities = await runnerQuery.ToListAsync(cancellationToken);
        var deletedHostedRunners = await dbContext.HostedRunners
            .AsNoTracking()
            .Where(x => x.DeletedAt != null)
            .Select(x => new { x.RunnerId, x.Name })
            .ToListAsync(cancellationToken);
        var deletedHostedRunnerIds = deletedHostedRunners
            .Where(x => x.RunnerId.HasValue)
            .Select(x => x.RunnerId!.Value)
            .ToHashSet();
        var deletedHostedRunnerNames = deletedHostedRunners
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        runnerEntities = runnerEntities
            .Where(x => !deletedHostedRunnerIds.Contains(x.Id) && !deletedHostedRunnerNames.Contains(x.Name))
            .ToList();

        var projectLookup = await BuildRunnerProjectLookup(
            runnerEntities.Select(r => r.Id),
            projectId,
            UserId,
            cancellationToken);
        var hostedRunnerLookup = await BuildHostedRunnerLookup(runnerEntities.Select(r => r.Id), cancellationToken);

        return await GetRunnersWithUpdatedStatus(runnerEntities, projectLookup, hostedRunnerLookup, cancellationToken);
    }

    public Runner Get(Guid id)
    {
        // Use authorization service query to check access (personal + project-based)
        var runner = authorizationService.GetAccessibleRunnersQuery()
            .SingleOrDefault(x => x.Id == id);

        // Because we update Runner's ConnectionId on the fly,
        // when retrieving data we get the cached version of it
        // with previous ConnectionId. There is no need to do the
        // same thing with other entities if they aren't updated
        // in the same way as the Runner entity.

        // Here are some ideas to fix the issue:
        // https://stackoverflow.com/a/51290890/778863
        // http://codethug.com/2016/02/19/Entity-Framework-Cache-Busting/

        // Solution 1. Reload the entity 
        dbContext.Entry(runner).Reload();

        // Solution 2. Detach the entity to remove it from context’s cache.
        // dbContext.Entry(entity).State = EntityState.Detached;
        // entity = dbContext.Runners.Find(id);

        return runner;
    }

    public async Task<int> Add(RunnerDetails runnerModel, CancellationToken cancellationToken = default)
    {
        logger.Debug("Adding runner {RunnerId} ({RunnerName}) for user {UserId}", runnerModel.Id, runnerModel.Name, UserId);

        if (UserId == Guid.Empty)
        {
            logger.Error("Cannot add runner: userId is empty/null. User context not properly set from authentication");
            logger.Error("This usually means API key authentication claims are not being properly converted to user context");
            throw new InvalidOperationException("User context not available. Authentication may not have been properly processed. UserId is empty.");
        }

        var purpose = RunnerPurposes.Normalize(runnerModel.Purpose);
        var capabilities = RunnerCapabilities.Normalize(runnerModel.Capabilities, purpose);
        if (RunnerCapabilities.RequiresAdministrator(capabilities) && !await IsAdministratorAsync(UserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only an administrator may register a Runner with privileged capabilities.");
        }

        var runner = new Runner()
        {
            Id = runnerModel.Id,
            Name = runnerModel.Name,
            Status = RunnerConnectivityStatuses.Unknown,
            Version = runnerModel.Version,
            MachineName = runnerModel.MachineName,
            IpAddress = runnerModel.IpAddress,
            Purpose = purpose,
            CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, purpose),
            InstallationId = runnerModel.InstallationId,
        };

        dbContext.Runners.Add(runner);

        var runnerUser = new RunnerUser
        {
            RunnerId = runner.Id,
            UserId = UserId
        };

        dbContext.RunnerUsers.Add(runnerUser);

        if (capabilities.Contains(RunnerCapabilities.HostedRunnerProvisioning, StringComparer.OrdinalIgnoreCase) &&
            runnerModel.RunnerHostId.HasValue)
        {
            var host = await dbContext.RunnerHosts
                .FirstOrDefaultAsync(x => x.Id == runnerModel.RunnerHostId.Value, cancellationToken);
            if (host == null)
            {
                throw new InvalidOperationException("The infrastructure runner host was not found.");
            }

            if (host.InfrastructureRunnerId.HasValue && host.InfrastructureRunnerId != runner.Id)
            {
                throw new InvalidOperationException("The runner host is already linked to another infrastructure runner.");
            }

            host.InfrastructureRunnerId = runner.Id;
            host.UpdatedAt = DateTime.UtcNow;
        }

        var result = await dbContext.SaveChangesAsync(cancellationToken);

        LinkHostedRunnerAfterRegistration(runner);
        result += await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.Runner,
            EntityId = runner.Id,
            EventType = AuditEventTypes.RunnerCreated,
            Message = $"Runner '{runner.Name}' was created",
            Outcome = AuditOutcomes.Success,
            ActorUserId = UserId,
            Metadata = new
            {
                runner.Name,
                runner.Version,
                runner.MachineName,
                runner.IpAddress,
                runner.Purpose,
                runnerModel.RunnerHostId,
                Changes = result
            }
        }, cancellationToken);

        logger.Debug("Runner created successfully: ID={RunnerId}", runner.Id);

        return result;
    }

    private async Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
                && (role.Name == UserRole.Administrator || role.Name == UserRole.SystemAdministrator)
            select userRole).AnyAsync(cancellationToken);
    }

    private void LinkHostedRunnerAfterRegistration(Runner runner)
    {
        var hostedRunner = dbContext.HostedRunners
            .Include(x => x.Deployments)
            .FirstOrDefault(x =>
                x.UserId == UserId &&
                x.Name == runner.Name &&
                x.RunnerId == null &&
                x.DeletedAt == null &&
                (x.Status == HostedRunnerStatuses.Pending || x.Status == HostedRunnerStatuses.Provisioning));

        if (hostedRunner == null)
        {
            return;
        }

        hostedRunner.RunnerId = runner.Id;
        hostedRunner.Status = HostedRunnerStatuses.Running;
        hostedRunner.ProvisionedAt ??= DateTime.UtcNow;
        hostedRunner.UpdatedAt = DateTime.UtcNow;

        var deployment = hostedRunner.Deployments
            .Where(x => x.Action == HostedRunnerDeploymentActions.Provision && x.Status == HostedRunnerDeploymentStatuses.Running)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefault();

        if (deployment != null)
        {
            deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
            deployment.FinishedAt = DateTime.UtcNow;
            deployment.Logs = "Runner registered successfully and is linked to this hosted runner.";
        }

        logger.Debug("Hosted runner {HostedRunnerId} linked to registered runner {RunnerId}", hostedRunner.Id, runner.Id);
    }

    private void LinkHostedRunnerAfterReconnect(Runner runner, Guid userId)
    {
        var hostedRunner = dbContext.HostedRunners
            .Include(x => x.Deployments)
            .FirstOrDefault(x =>
                x.UserId == userId &&
                x.Name == runner.Name &&
                x.RunnerId == null &&
                x.DeletedAt == null &&
                (x.Status == HostedRunnerStatuses.Pending
                    || x.Status == HostedRunnerStatuses.Provisioning
                    || x.Status == HostedRunnerStatuses.Failed));

        if (hostedRunner == null)
        {
            return;
        }

        hostedRunner.RunnerId = runner.Id;
        hostedRunner.Status = HostedRunnerStatuses.Running;
        hostedRunner.ProvisionedAt ??= DateTime.UtcNow;
        hostedRunner.UpdatedAt = DateTime.UtcNow;
        hostedRunner.LastError = null;

        var deployment = hostedRunner.Deployments
            .Where(x =>
                (x.Action == HostedRunnerDeploymentActions.Provision || x.Action == HostedRunnerDeploymentActions.Reprovision)
                && (x.Status == HostedRunnerDeploymentStatuses.Running || x.Status == HostedRunnerDeploymentStatuses.Succeeded))
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefault();

        if (deployment != null)
        {
            deployment.Status = HostedRunnerDeploymentStatuses.Succeeded;
            deployment.FinishedAt = DateTime.UtcNow;
            deployment.Logs = "Runner connected successfully and is linked to this hosted runner.";
            deployment.Error = null;
        }

        logger.Warning("Hosted runner {HostedRunnerId} re-linked to runner {RunnerId} after reconnect", hostedRunner.Id, runner.Id);
    }

    public async Task UpdateConnection(Guid runnerId, Guid userId, RunnerDetails runnerConnection, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(runnerId, userId, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {runnerId}");
        }

        var runner = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == runnerId);

        if (runner == null)
        {
            throw new InvalidOperationException($"Runner {runnerId} not found");
        }

        var now = DateTime.UtcNow;

        await CloseOpenSessionsAsync(
            runner.Id,
            now,
            RunnerSessionDisconnectReasons.SupersededConnection,
            cancellationToken);

        runner.ConnectionId = runnerConnection.ConnectionId;
        runner.LastConnectedAtUtc = now;
        runner.LastHeartbeatAtUtc = now;
        runner.Status = RunnerConnectivityStatuses.Online;
        runner.Version = runnerConnection.Version;
        runner.MachineName = runnerConnection.MachineName;
        runner.IpAddress = runnerConnection.IpAddress;
        runner.ApiInstanceId = runnerConnection.ApiInstanceId ?? apiInstanceIdentityService.ApiInstanceId;
        runner.ApiColor = runnerConnection.ApiColor ?? apiInstanceIdentityService.ApiColor;

        // Mark entity as modified
        dbContext.Entry(runner).State = EntityState.Modified;

        // Update runner entity
        dbContext.Update(runner);

        // Save changes
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.RunnerConnectionSessions.Add(new RunnerConnectionSession
        {
            Id = Guid.NewGuid(),
            RunnerId = runner.Id,
            ConnectionId = runnerConnection.ConnectionId!,
            ConnectedAtUtc = now,
            ApiInstanceId = runner.ApiInstanceId,
            ApiColor = runner.ApiColor
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        LinkHostedRunnerAfterReconnect(runner, userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await managedRunnerAppAssignmentService.AssignEligibleAppsAsync(userId, runner.Id, cancellationToken);

        await WriteRunnerApplicationEventsAsync(
            runner.Id,
            ApplicationEventLevels.Info,
            ApplicationEventCategories.Runtime,
            ApplicationEventTypes.RunnerConnected,
            "Runner connected.",
            new
            {
                runner.Name,
                runner.ConnectionId,
                runner.MachineName,
                runner.ApiInstanceId
            },
            userId,
            cancellationToken);
    }

    public async Task RecordHeartbeatAsync(Guid runnerId, Guid userId, RunnerDetails runnerHeartbeat, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanUpdateRunnerAsync(runnerId, userId, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {runnerId}");
        }

        var runner = await dbContext.Runners
            .SingleOrDefaultAsync(x => x.Id == runnerId, cancellationToken);

        if (runner == null)
        {
            throw new InvalidOperationException($"Runner {runnerId} not found");
        }

        // A runner can still deliver a heartbeat while its old blue SignalR
        // connection is closing after a green connection has registered. Do
        // not let that stale session overwrite the current connection state.
        if (!string.Equals(runner.ConnectionId, runnerHeartbeat.ConnectionId, StringComparison.Ordinal))
        {
            logger.Debug(
                "Ignoring heartbeat from stale runner connection {ConnectionId} for runner {RunnerId}; current connection is {CurrentConnectionId}",
                runnerHeartbeat.ConnectionId,
                runner.Id,
                runner.ConnectionId);
            return;
        }

        runner.ConnectionId = runnerHeartbeat.ConnectionId;
        runner.LastHeartbeatAtUtc = DateTime.UtcNow;
        runner.Status = RunnerConnectivityStatuses.Online;
        runner.Version = runnerHeartbeat.Version;
        runner.MachineName = runnerHeartbeat.MachineName;
        runner.IpAddress = runnerHeartbeat.IpAddress;
        runner.ApiInstanceId = runnerHeartbeat.ApiInstanceId ?? apiInstanceIdentityService.ApiInstanceId;
        runner.ApiColor = runnerHeartbeat.ApiColor ?? apiInstanceIdentityService.ApiColor;

        if (string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase))
        {
            var host = await dbContext.RunnerHosts
                .SingleOrDefaultAsync(x => x.InfrastructureRunnerId == runner.Id, cancellationToken);
            if (host != null)
            {
                host.LastHeartbeatAtUtc = runner.LastHeartbeatAtUtc;
                if (string.IsNullOrWhiteSpace(host.PublicIp) && !string.IsNullOrWhiteSpace(runner.IpAddress))
                {
                    host.PublicIp = runner.IpAddress;
                }
            }
        }

        dbContext.Entry(runner).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkDisconnectedAsync(Guid runnerId, string connectionId, CancellationToken cancellationToken)
    {
        var runner = await dbContext.Runners
            .SingleOrDefaultAsync(x => x.Id == runnerId, cancellationToken);

        if (runner == null)
        {
            return;
        }

        if (!string.Equals(runner.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            return;
        }

        runner.ConnectionId = null;
        runner.LastDisconnectedAtUtc = DateTime.UtcNow;
        runner.Status = RunnerConnectivityStatuses.Offline;

        dbContext.Entry(runner).State = EntityState.Modified;
        await CloseMatchingOpenSessionAsync(
            runner.Id,
            connectionId,
            runner.LastDisconnectedAtUtc.Value,
            RunnerSessionDisconnectReasons.GracefulDisconnect,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await WriteRunnerApplicationEventsAsync(
            runner.Id,
            ApplicationEventLevels.Warning,
            ApplicationEventCategories.Runtime,
            ApplicationEventTypes.RunnerDisconnected,
            "Runner disconnected.",
            new
            {
                runner.Name,
                runner.MachineName,
                Reason = RunnerSessionDisconnectReasons.GracefulDisconnect
            },
            UserId,
            cancellationToken);
    }

    public async Task<int> CloseStaleConnectionSessionsAsync(CancellationToken cancellationToken)
    {
        var thresholdUtc = DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold;
        var staleRunners = await dbContext.Runners
            .Where(x => x.ConnectionId != null)
            .Where(x => x.LastHeartbeatAtUtc == null || x.LastHeartbeatAtUtc < thresholdUtc)
            .ToListAsync(cancellationToken);

        if (staleRunners.Count == 0)
        {
            return 0;
        }

        var staleRunnerIds = staleRunners.Select(x => x.Id).ToList();
        var openSessions = await dbContext.RunnerConnectionSessions
            .Where(x => staleRunnerIds.Contains(x.RunnerId) && x.DisconnectedAtUtc == null)
            .ToListAsync(cancellationToken);

        var sessionsByRunnerId = openSessions
            .GroupBy(x => x.RunnerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var now = DateTime.UtcNow;
        var closedCount = 0;

        foreach (var runner in staleRunners)
        {
            if (string.IsNullOrWhiteSpace(runner.ConnectionId))
            {
                continue;
            }

            if (sessionsByRunnerId.TryGetValue(runner.Id, out var sessions))
            {
                foreach (var session in sessions.Where(x => string.Equals(x.ConnectionId, runner.ConnectionId, StringComparison.Ordinal)))
                {
                    session.DisconnectedAtUtc = now;
                    session.DisconnectReason = RunnerSessionDisconnectReasons.HeartbeatTimeout;
                    closedCount++;
                }
            }

            runner.ConnectionId = null;
            runner.LastDisconnectedAtUtc = now;
            runner.Status = RunnerConnectivityStatuses.Offline;
            dbContext.Entry(runner).State = EntityState.Modified;
        }

        if (closedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.Warning("Closed {SessionCount} stale runner connection session(s) after heartbeat timeout", closedCount);

            foreach (var runner in staleRunners)
            {
                await WriteRunnerApplicationEventsAsync(
                    runner.Id,
                    ApplicationEventLevels.Error,
                    ApplicationEventCategories.Runtime,
                    ApplicationEventTypes.RunnerDisconnected,
                    "Runner disconnected after heartbeat timeout.",
                    new
                    {
                        runner.Name,
                        Reason = RunnerSessionDisconnectReasons.HeartbeatTimeout
                    },
                    UserId,
                    cancellationToken);
            }
        }

        return closedCount;
    }

    public async Task<int> RequestConnectedRunnersReconnectWhenIdleAsync(string reason, CancellationToken cancellationToken)
    {
        var connectedRunners = await dbContext.Runners
            .AsNoTracking()
            .Where(x => x.ConnectionId != null)
            .Where(x => x.ApiInstanceId == apiInstanceIdentityService.ApiInstanceId)
            .Select(x => new { x.Id, x.Name, x.ConnectionId })
            .ToListAsync(cancellationToken);

        var signaled = 0;
        foreach (var runner in connectedRunners)
        {
            try
            {
                await automatonHub.Clients.Client(runner.ConnectionId!)
                    .SendAsync("ReconnectWhenIdle", reason, cancellationToken);
                signaled++;
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to request reconnect-when-idle for runner {RunnerId} ({RunnerName})", runner.Id, runner.Name);
            }
        }

        logger.Debug(
            "Requested reconnect-when-idle for {RunnerCount} connected runners owned by API instance {ApiInstanceId} color {ApiColor}",
            signaled,
            apiInstanceIdentityService.ApiInstanceId,
            apiInstanceIdentityService.ApiColor ?? "(unset)");
        return signaled;
    }

    public async Task<RunnerDrainStatus> GetReconnectDrainStatusAsync(CancellationToken cancellationToken)
    {
        var connectedRunnerCount = await dbContext.Runners
            .AsNoTracking()
            .CountAsync(x =>
                x.ConnectionId != null &&
                x.ApiInstanceId == apiInstanceIdentityService.ApiInstanceId,
                cancellationToken);

        return new RunnerDrainStatus
        {
            ConnectedRunnerCount = connectedRunnerCount,
            Drained = connectedRunnerCount == 0
        };
    }

    public async Task Update(Guid id, RunnerDetails runner, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(id, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {id}");
        }

        var entity = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity == null)
        {
            throw new InvalidOperationException($"Runner {id} not found");
        }

        var capabilities = RunnerCapabilities.Normalize(runner.Capabilities, entity.Purpose);
        if (RunnerCapabilities.RequiresAdministrator(capabilities) && !await IsAdministratorAsync(UserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only an administrator may register a Runner with privileged capabilities.");
        }

        entity.Name = runner.Name;
        entity.Version = runner.Version;
        entity.MachineName = runner.MachineName;
        entity.IpAddress = runner.IpAddress;
        entity.CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, entity.Purpose);
        entity.InstallationId = runner.InstallationId ?? entity.InstallationId;

        // Mark entity as modified
        dbContext.Entry(entity).State = EntityState.Modified;

        // Update runner entity
        dbContext.Update(entity);

        // Save changes
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public bool Exists(string name)
    {
        var exists = dbContext.Runners.Any(x =>
            x.Name.ToLower() == name.ToLower() &&
            x.RunnerUsers.Any(x => x.UserId == UserId));

        return exists;
    }

    public bool ExistsById(Guid id)
    {
        var exists = dbContext.Runners.Any(x =>
         x.Id == id &&
            x.RunnerUsers.Any(x => x.UserId == UserId));

        return exists;
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken)
    {
        // Check if user can delete this runner (must be personal owner AND not part of any project)
        if (!await authorizationService.CanDeleteRunnerAsync(id, cancellationToken))
        {
            // Check if it's because the runner is part of a project
            var isPartOfProject = await dbContext.ProjectRunners
                .AnyAsync(pr => pr.RunnerId == id, cancellationToken);

            if (isPartOfProject)
            {
                throw new InvalidOperationException($"Cannot delete runner {id} because it is part of one or more projects. Remove it from all projects first.");
            }

            throw new UnauthorizedAccessException($"You do not have permission to delete runner {id}");
        }

        var entity = dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefault(x => x.Id == id);

        if (entity != null)
        {
            var auditMetadata = new
            {
                entity.Name,
                entity.Version,
                entity.MachineName,
                entity.IpAddress
            };

            var isHostedRunner = await dbContext.HostedRunners
                .AnyAsync(x => x.RunnerId == id && x.DeletedAt == null, cancellationToken);

            if (isHostedRunner)
            {
                throw new InvalidOperationException("Hosted runners cannot be deleted from the generic runners page. Restart the hosted runner instead.");
            }

            dbContext.Runners.Remove(entity);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Deleted,
                EntityType = AuditEntityTypes.Runner,
                EntityId = id,
                EventType = AuditEventTypes.RunnerDeleted,
                Message = $"Runner '{entity.Name}' was deleted",
                Outcome = AuditOutcomes.Success,
                ActorUserId = UserId,
                Metadata = auditMetadata
            }, cancellationToken);
        }
    }

    public async Task<IEnumerable<RunnerFlowResult>> ExecuteFlow(Guid flowId, IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var results = new List<RunnerFlowResult>();

        foreach (var runnerId in runnerIds)
        {
            RunnerFlowResult result;
            var runner = Get(runnerId);
            var client = automatonHub.Clients.Client(runner.ConnectionId);

            try
            {
                var response = await client.InvokeAsync<WorkflowExecution>(AutomatonHubMethods.RunWorkflow, flowId, cancellationToken);

                result = GetSuccessfulFlowResult(flowId: flowId, runnerId: runnerId, response);
            }
            catch (Exception ex)
            {
                logger.ForContext("FlowId", flowId)
                      .ForContext("RunnerId", runnerId)
                      .Error(ex, "An error happened when executing flow on runner");

                result = GetInvalidFlowResult(flowId: flowId, runnerId: runnerId);
            }

            results.Add(result);
        }

        return results;
    }

    public async Task<IEnumerable<RunnerFlowResult>> ExecuteFlow(Guid flowId, IEnumerable<Guid> runnerIds, Guid userId, CancellationToken cancellationToken)
    {
        var results = new List<RunnerFlowResult>();

        foreach (var runnerId in runnerIds)
        {
            RunnerFlowResult result;
            var runner = Get(runnerId);
            var client = automatonHub.Clients.Client(runner.ConnectionId);

            try
            {
                var response = await client.InvokeAsync<WorkflowExecution>(AutomatonHubMethods.RunWorkflow, flowId, cancellationToken);

                result = GetSuccessfulFlowResult(flowId: flowId, runnerId: runnerId, response);
            }
            catch (Exception ex)
            {
                logger.ForContext("FlowId", flowId)
                      .ForContext("RunnerId", runnerId)
                      .Error(ex, "An error happened when executing flow on runner");

                result = GetInvalidFlowResult(flowId: flowId, runnerId: runnerId);
            }

            results.Add(result);
        }

        return results;
    }

    private async Task<Dictionary<Guid, (Guid ProjectId, string ProjectName)>> BuildRunnerProjectLookup(
        IEnumerable<Guid> runnerIds,
        Guid? projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var ids = runnerIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, (Guid ProjectId, string ProjectName)>();
        }

        var query = dbContext.ProjectRunners
            .Include(x => x.Project)
            .Where(x => ids.Contains(x.RunnerId));

        if (projectId.HasValue)
        {
            query = query.Where(x => x.ProjectId == projectId.Value);
        }
        else
        {
            query = query.Where(x => x.Project.Members.Any(pm => pm.UserId == userId && pm.IsActive));
        }

        var links = await query.ToListAsync(cancellationToken);

        return links
            .GroupBy(x => x.RunnerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var first = g.First();
                    return (first.ProjectId, first.Project.DisplayName);
                });
    }

    private async Task<IEnumerable<RunnerDetails>> GetRunnersWithUpdatedStatus(
        IEnumerable<Runner> runnerEntities,
        IDictionary<Guid, (Guid ProjectId, string ProjectName)> projectLookup,
        IDictionary<Guid, Guid> hostedRunnerLookup,
        CancellationToken cancellationToken)
    {
        var runners = RunnerMapper
            .ToRunnerDetailsList(runnerEntities, RunnerStatus.None, projectLookup, hostedRunnerLookup)
            .ToList();
        var sessionStats = await BuildRunnerSessionStatsAsync(runners.Select(x => x.Id), cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var runner in runners)
        {
            runner.Status = RunnerPresenceEvaluator.DeriveStatus(runner.ConnectionId, runner.LastHeartbeatAtUtc, now);

            if (sessionStats.TryGetValue(runner.Id, out var stats))
            {
                runner.CurrentSessionConnectedAtUtc = stats.CurrentSessionConnectedAtUtc;
                runner.ConnectionSessionCount = stats.ConnectionSessionCount;
                runner.TotalConnectedDurationSeconds = stats.TotalConnectedDurationSeconds;
            }
        }

        return runners;
    }

    private async Task<IDictionary<Guid, Guid>> BuildHostedRunnerLookup(IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var ids = runnerIds.ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        return await dbContext.HostedRunners
            .AsNoTracking()
            .Where(x => x.RunnerId != null && ids.Contains(x.RunnerId.Value) && x.DeletedAt == null)
            .ToDictionaryAsync(x => x.RunnerId!.Value, x => x.Id, cancellationToken);
    }

    private RunnerFlowResult GetSuccessfulFlowResult(Guid flowId, Guid runnerId, WorkflowExecution flowExecution)
    {
        return new RunnerFlowResult
        {
            FlowId = flowId,
            RunnerId = runnerId,
            Started = flowExecution.Started,
            Finished = flowExecution.Finished,
            Status = flowExecution.Status,

            // ✅ Include OutputVariables from flow execution
            OutputVariables = flowExecution.OutputVariables
                .ToDictionary(k => k.Key, v => v.Value.Value),

            // ✅ Include execution logs for debugging
            Logs = flowExecution.Logs
                .Select(log => $"[{log.Timestamp:HH:mm:ss}] [{log.Level}] {log.Message}")
                .ToList()
        };
    }

    private RunnerFlowResult GetInvalidFlowResult(Guid flowId, Guid runnerId)
    {
        return new RunnerFlowResult
        {
            FlowId = flowId,
            RunnerId = runnerId,
            Started = DateTime.MinValue,
            Finished = DateTime.MinValue,
            Status = WorkflowStatus.None
        };
    }

    /// <summary>
    /// Get script engine settings for a specific runner
    /// </summary>
    public async Task<RunnerScriptEngineSettings?> GetScriptEngineSettings(Guid id, CancellationToken cancellationToken)
    {
        // Check if user can access this runner (personal owner OR project member)
        if (!await authorizationService.CanAccessRunnerAsync(id, cancellationToken))
        {
            return null;
        }

        var runner = await dbContext.Runners
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (runner == null || string.IsNullOrWhiteSpace(runner.ScriptEngineSettings))
        {
            return null;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<RunnerScriptEngineSettings>(
                runner.ScriptEngineSettings,
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                   );

            return settings;
        }
        catch (JsonException ex)
        {
            logger.Warning(ex, "Failed to deserialize script engine settings for runner {RunnerId}", id);
            return null;
        }
    }

    /// <summary>
    /// Update script engine settings for a specific runner
    /// </summary>
    public async Task UpdateScriptEngineSettings(Guid id, string? settingsJson, CancellationToken cancellationToken)
    {
        // Check if user can update this runner (personal owner OR project member with Developer/Admin role)
        if (!await authorizationService.CanUpdateRunnerAsync(id, cancellationToken))
        {
            throw new UnauthorizedAccessException($"You do not have permission to update runner {id}");
        }

        var runner = await dbContext.Runners
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (runner == null)
        {
            throw new InvalidOperationException($"Runner {id} not found");
        }

        runner.ScriptEngineSettings = settingsJson;

        dbContext.Entry(runner).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Debug("Updated script engine settings for runner {RunnerId}", id);
    }

    public async Task<RunnerDetails?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        // Use authorization service query to check access (personal + project-based)
        var runner = await authorizationService.GetAccessibleRunnersQuery()
            .FirstOrDefaultAsync(x => x.Name.ToLower() == name.ToLower(), cancellationToken);

        if (runner == null)
        {
            return null;
        }

        var projectLookup = await BuildRunnerProjectLookup(
            new[] { runner.Id },
            null,
            UserId,
            cancellationToken);

        if (projectLookup.TryGetValue(runner.Id, out var link))
        {
            var hostedRunnerId = await dbContext.HostedRunners
                .AsNoTracking()
                .Where(x => x.RunnerId == runner.Id && x.DeletedAt == null)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var details = RunnerMapper.ToRunnerDetails(
                runner,
                RunnerPresenceEvaluator.DeriveStatus(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow),
                link.ProjectId,
                link.ProjectName,
                hostedRunnerId);
            await PopulateRunnerSessionStatsAsync(details, cancellationToken);
            return details;
        }

        var fallbackHostedRunnerId = await dbContext.HostedRunners
            .AsNoTracking()
            .Where(x => x.RunnerId == runner.Id && x.DeletedAt == null)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var fallbackDetails = RunnerMapper.ToRunnerDetails(
            runner,
            RunnerPresenceEvaluator.DeriveStatus(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow),
            null,
            null,
            fallbackHostedRunnerId);
        await PopulateRunnerSessionStatsAsync(fallbackDetails, cancellationToken);
        return fallbackDetails;
    }

    private async Task PopulateRunnerSessionStatsAsync(RunnerDetails runner, CancellationToken cancellationToken)
    {
        var stats = await BuildRunnerSessionStatsAsync(new[] { runner.Id }, cancellationToken);
        if (!stats.TryGetValue(runner.Id, out var value))
        {
            return;
        }

        runner.CurrentSessionConnectedAtUtc = value.CurrentSessionConnectedAtUtc;
        runner.ConnectionSessionCount = value.ConnectionSessionCount;
        runner.TotalConnectedDurationSeconds = value.TotalConnectedDurationSeconds;
        runner.CurrentSessionId = value.CurrentSessionId;
        runner.LastDisconnectReason = value.LastDisconnectReason;
    }

    private async Task<Dictionary<Guid, RunnerSessionStats>> BuildRunnerSessionStatsAsync(IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        var ids = runnerIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, RunnerSessionStats>();
        }

        var runnerStates = await dbContext.Runners
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new RunnerSessionPresenceState(
                x.Id,
                x.ConnectionId,
                x.LastHeartbeatAtUtc))
            .ToDictionaryAsync(x => x.RunnerId, cancellationToken);

        var sessions = await dbContext.RunnerConnectionSessions
            .AsNoTracking()
            .Where(x => ids.Contains(x.RunnerId))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        return sessions
            .GroupBy(x => x.RunnerId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    runnerStates.TryGetValue(g.Key, out var runnerState);
                    var totalSeconds = g.Sum(session =>
                    {
                        var sessionEnd = session.DisconnectedAtUtc ?? GetOpenSessionEffectiveEnd(session, runnerState, now);
                        var duration = sessionEnd - session.ConnectedAtUtc;
                        return duration > TimeSpan.Zero ? Convert.ToInt64(duration.TotalSeconds) : 0L;
                    });
                    var currentSession = g
                        .Where(x => x.DisconnectedAtUtc == null)
                        .OrderByDescending(x => x.ConnectedAtUtc)
                        .FirstOrDefault();
                    var currentSessionConnectedAtUtc =
                        runnerState != null && RunnerPresenceEvaluator.IsOnline(runnerState.ConnectionId, runnerState.LastHeartbeatAtUtc, now)
                            ? currentSession?.ConnectedAtUtc
                            : null;

                    return new RunnerSessionStats(
                        totalSeconds,
                        g.Count(),
                        currentSessionConnectedAtUtc,
                        currentSession?.Id,
                        g.Where(x => x.DisconnectedAtUtc.HasValue)
                            .OrderByDescending(x => x.DisconnectedAtUtc)
                            .Select(x => x.DisconnectReason)
                            .FirstOrDefault());
                });
    }

    private static DateTime GetOpenSessionEffectiveEnd(
        RunnerConnectionSession session,
        RunnerSessionPresenceState? runnerState,
        DateTime utcNow)
    {
        if (runnerState == null)
        {
            return session.ConnectedAtUtc;
        }

        return RunnerPresenceEvaluator.IsOnline(runnerState.ConnectionId, runnerState.LastHeartbeatAtUtc, utcNow)
            ? utcNow
            : (runnerState.LastHeartbeatAtUtc ?? session.ConnectedAtUtc);
    }

    private async Task CloseOpenSessionsAsync(Guid runnerId, DateTime disconnectedAtUtc, string disconnectReason, CancellationToken cancellationToken)
    {
        var openSessions = await dbContext.RunnerConnectionSessions
            .Where(x => x.RunnerId == runnerId && x.DisconnectedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var session in openSessions)
        {
            session.DisconnectedAtUtc = disconnectedAtUtc;
            session.DisconnectReason = disconnectReason;
        }
    }

    private async Task CloseMatchingOpenSessionAsync(Guid runnerId, string connectionId, DateTime disconnectedAtUtc, string disconnectReason, CancellationToken cancellationToken)
    {
        var session = await dbContext.RunnerConnectionSessions
            .Where(x => x.RunnerId == runnerId && x.ConnectionId == connectionId && x.DisconnectedAtUtc == null)
            .OrderByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            return;
        }

        session.DisconnectedAtUtc = disconnectedAtUtc;
        session.DisconnectReason = disconnectReason;
    }

    private sealed record RunnerSessionStats(
        long TotalConnectedDurationSeconds,
        int ConnectionSessionCount,
        DateTime? CurrentSessionConnectedAtUtc,
        Guid? CurrentSessionId,
        string? LastDisconnectReason);

    private sealed record RunnerSessionPresenceState(
        Guid RunnerId,
        string? ConnectionId,
        DateTime? LastHeartbeatAtUtc);

    private async Task WriteRunnerApplicationEventsAsync(
        Guid runnerId,
        string level,
        string category,
        string eventType,
        string message,
        object? metadata,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var appIds = await dbContext.WebApps
            .AsNoTracking()
            .Where(x => x.ExecutionRunnerId == runnerId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var appId in appIds)
        {
            await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
            {
                AppId = appId,
                Level = level,
                Category = category,
                EventType = eventType,
                Message = message,
                Metadata = metadata,
                ActorUserId = actorUserId
            }, cancellationToken);
        }
    }
}

public record RunnerDrainStatus
{
    public int ConnectedRunnerCount { get; init; }
    public bool Drained { get; init; }
}
