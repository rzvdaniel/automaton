using System.Data;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

public sealed class InfrastructureRunnerHostApprovalService(
    ApplicationDbContext db,
    IAuditEventService auditEventService,
    ILogger<InfrastructureRunnerHostApprovalService> logger,
    IHubContext<AutomatonHub> automatonHub)
{
    public async Task<RunnerHost> ApproveAsync(Guid hostId, Guid approvedByUserId, CancellationToken cancellationToken)
    {
        RunnerHost? host = null;
        var now = DateTime.UtcNow;
        await ExecuteAtomicallyAsync(async () =>
        {
            host = await LoadHostAsync(hostId, cancellationToken)
                ?? throw new KeyNotFoundException("The Runner host was not found.");

            if (host.ProvisioningStatus != RunnerHostProvisioningStatuses.AwaitingApproval)
                throw new InvalidOperationException("Only a host awaiting approval can be approved.");
            if (!host.ValidatedAtUtc.HasValue)
                throw new InvalidOperationException("The Runner host must pass validation before approval.");
            if (host.InfrastructureRunner == null || host.InfrastructureRunnerId != host.InfrastructureRunner.Id)
                throw new InvalidOperationException("The Runner host has no correctly bound Infrastructure Runner.");
            if (!string.Equals(host.InfrastructureRunner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The linked Runner is not an Infrastructure Runner.");
            if (host.InfrastructureRunner.ApiKey == null || !host.InfrastructureRunner.ApiKey.IsActive)
                throw new InvalidOperationException("The Infrastructure Runner credential is not active.");
            if (!RunnerPresenceEvaluator.IsOnline(
                    host.InfrastructureRunner.ConnectionId,
                    host.InfrastructureRunner.LastHeartbeatAtUtc,
                    now))
                throw new InvalidOperationException("The Infrastructure Runner must be online before approval.");

            host.ProvisioningStatus = RunnerHostProvisioningStatuses.Ready;
            host.HostedRunnerSchedulingEnabled = true;
            host.ApprovedAtUtc = now;
            host.ApprovedByUserId = approvedByUserId;
            host.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Enabled,
            EntityType = AuditEntityTypes.Runner,
            EntityId = host!.InfrastructureRunnerId!.Value,
            EventType = AuditEventTypes.InfrastructureRunnerHostApproved,
            Message = $"Infrastructure Runner host '{host.Name}' was approved for managed Runner scheduling.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = approvedByUserId,
            Metadata = new { HostId = host.Id, host.InfrastructureRunnerId }
        }, cancellationToken);

        return host!;
    }

    public async Task<RunnerHost?> DisableSchedulingAsync(Guid hostId, Guid adminUserId, CancellationToken cancellationToken)
    {
        var host = await LoadHostAsync(hostId, cancellationToken);
        if (host == null) return null;

        host.HostedRunnerSchedulingEnabled = false;
        host.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Disabled,
            EntityType = AuditEntityTypes.Runner,
            EntityId = host.InfrastructureRunnerId ?? host.Id,
            EventType = AuditEventTypes.InfrastructureRunnerHostSchedulingDisabled,
            Message = $"Managed Runner scheduling was disabled for host '{host.Name}'.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = adminUserId,
            Metadata = new { HostId = host.Id }
        }, cancellationToken);

        return host;
    }

    public async Task<RunnerHost?> RevokeAsync(Guid hostId, Guid adminUserId, CancellationToken cancellationToken)
    {
        var host = await LoadHostAsync(hostId, cancellationToken);
        if (host == null) return null;
        if (host.InfrastructureRunner == null)
            throw new InvalidOperationException("The Runner host has no linked Infrastructure Runner.");

        var runner = host.InfrastructureRunner;
        var connectionId = runner.ConnectionId;
        await ExecuteAtomicallyAsync(async () =>
        {
            if (runner.ApiKey != null)
                runner.ApiKey.IsActive = false;
            runner.ConnectionId = null;
            runner.LastDisconnectedAtUtc = DateTime.UtcNow;
            runner.Status = RunnerConnectivityStatuses.Offline;
            host.HostedRunnerSchedulingEnabled = false;
            host.Status = RunnerHostStatuses.Disabled;
            host.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            try
            {
                await automatonHub.Clients.Client(connectionId)
                    .SendAsync("ReconnectWhenIdle", "Infrastructure Runner credential revoked.", cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not signal revoked Infrastructure Runner {RunnerId} to disconnect", runner.Id);
            }
        }

        logger.LogInformation(
            "Infrastructure Runner {RunnerId} revoked for host {RunnerHostId}; previous connection present: {HadConnection}",
            runner.Id,
            host.Id,
            !string.IsNullOrWhiteSpace(connectionId));
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Disabled,
            EntityType = AuditEntityTypes.Runner,
            EntityId = runner.Id,
            EventType = AuditEventTypes.InfrastructureRunnerRevoked,
            Message = $"Infrastructure Runner '{runner.Name}' was revoked for host '{host.Name}'.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = adminUserId,
            Metadata = new { HostId = host.Id, RunnerId = runner.Id }
        }, cancellationToken);

        return host;
    }

    private Task<RunnerHost?> LoadHostAsync(Guid hostId, CancellationToken cancellationToken)
        => db.RunnerHosts
            .Include(x => x.InfrastructureRunner)
            .ThenInclude(x => x!.ApiKey)
            .FirstOrDefaultAsync(x => x.Id == hostId, cancellationToken);

    private async Task ExecuteAtomicallyAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            await action();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await action();
        await transaction.CommitAsync(cancellationToken);
    }
}
