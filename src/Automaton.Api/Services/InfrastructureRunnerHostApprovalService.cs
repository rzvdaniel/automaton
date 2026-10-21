using System.Data;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class InfrastructureRunnerHostApprovalService(
    ApplicationDbContext db,
    IAuditEventService auditEventService,
    ILogger<InfrastructureRunnerHostApprovalService> logger,
    RunnerExecutionRevocationService executions)
{
    public async Task<RunnerHost> ApproveAsync(Guid hostId, Guid approvedByUserId, CancellationToken cancellationToken)
    {
        RunnerHost? host = null;
        var now = DateTime.UtcNow;
        await ExecuteAtomicallyAsync(async () =>
        {
            host = await LoadHostAsync(hostId, cancellationToken)
                ?? throw new KeyNotFoundException("The Runner host was not found.");
            ValidateApprovalRequirements(host, now);

            host.ProvisioningStatus = RunnerHostProvisioningStatuses.Ready;
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
            Message = $"Infrastructure Runner host '{host.Name}' was approved.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = approvedByUserId,
            Metadata = new { HostId = host.Id, host.InfrastructureRunnerId }
        }, cancellationToken);

        return host!;
    }

    private static void ValidateApprovalRequirements(RunnerHost host, DateTime now)
    {
        if (host.ProvisioningStatus != RunnerHostProvisioningStatuses.AwaitingApproval)
            throw new InvalidOperationException("Only a host awaiting approval can be approved.");
        if (!host.ValidatedAtUtc.HasValue)
            throw new InvalidOperationException("The Runner host must pass validation before approval.");
        if (host.RuntimeConnectivityMode == RunnerHostRuntimeConnectivityModes.PublicHostPort &&
            !RunnerHostRuntimeAddressValidator.TryValidatePublicIpv4(host.PublicIp, out var addressError))
            throw new InvalidOperationException(addressError);
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
    }

    public async Task<RunnerHost?> RevokeAsync(Guid hostId, Guid adminUserId, CancellationToken cancellationToken)
    {
        var host = await LoadHostAsync(hostId, cancellationToken);
        if (host == null) return null;
        if (host.InfrastructureRunner == null)
            throw new InvalidOperationException("The Runner host has no linked Infrastructure Runner.");

        var runner = host.InfrastructureRunner;
        await ExecuteAtomicallyAsync(async () =>
        {
            await RevokeWithinTransactionAsync(host, runner, cancellationToken);
        }, cancellationToken);

        await executions.RevokeAsync(runner.Id, cancellationToken);

        logger.LogInformation(
            "Infrastructure Runner {RunnerId} revoked for host {RunnerHostId}",
            runner.Id,
            host.Id);
        await WriteRevocationAuditAsync(host, runner, adminUserId, cancellationToken);

        return host;
    }

    private async Task RevokeWithinTransactionAsync(
        RunnerHost host,
        Runner runner,
        CancellationToken cancellationToken)
    {
        if (runner.ApiKey is not null)
            runner.ApiKey.IsActive = false;
        runner.ConnectionId = null;
        runner.LastDisconnectedAtUtc = DateTime.UtcNow;
        runner.Status = RunnerConnectivityStatuses.Offline;
        var assignedTargets = await db.AppDeploymentTargets
            .Where(target => target.ExecutionRunnerId == runner.Id)
            .ToListAsync(cancellationToken);
        foreach (var target in assignedTargets)
        {
            target.ExecutionRunnerId = null;
            target.ExecutionRunner = null;
            target.GenericRuntimeDeployed = false;
            target.Status = DeploymentTargetStatus.Unassigned;
            target.UpdatedAt = DateTimeOffset.UtcNow;
        }
        host.Status = RunnerHostStatuses.Disabled;
        host.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task WriteRevocationAuditAsync(
        RunnerHost host,
        Runner runner,
        Guid adminUserId,
        CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
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
