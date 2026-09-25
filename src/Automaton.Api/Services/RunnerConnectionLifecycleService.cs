using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Applies Runner connection and heartbeat reports while preserving authoritative identity and lifecycle state.
/// </summary>
public sealed class RunnerConnectionLifecycleService(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    ApiInstanceIdentityService apiInstanceIdentityService,
    RunnerConnectionSessionService connectionSessionService,
    RunnerRegistrationService runnerRegistrationService,
    ManagedRunnerAppAssignmentService managedRunnerAppAssignmentService,
    HostedRunnerRestartCompletionService restartCompletionService)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<RunnerConnectionLifecycleService>();

    public async Task UpdateConnectionAsync(
        Guid runnerId,
        Guid userId,
        RunnerDetails runnerConnection,
        CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanUpdateRunnerAsync(runnerId, userId, cancellationToken))
            throw new UnauthorizedAccessException($"You do not have permission to update runner {runnerId}");

        var runner = await dbContext.Runners
            .Include(x => x.RunnerUsers)
            .SingleOrDefaultAsync(x => x.Id == runnerId, cancellationToken);
        if (runner == null)
            throw new InvalidOperationException($"Runner {runnerId} not found");

        var now = DateTime.UtcNow;
        await connectionSessionService.CloseOpenSessionsAsync(
            runner.Id, now, RunnerSessionDisconnectReasons.SupersededConnection, cancellationToken);
        ApplyConnectionDetails(runner, runnerConnection, now);
        dbContext.Entry(runner).State = EntityState.Modified;
        dbContext.Update(runner);
        await dbContext.SaveChangesAsync(cancellationToken);
        await RecordRunnerConnectionSessionAsync(runner, runnerConnection.ConnectionId!, now, cancellationToken);
        await CompleteHostedRunnerReconnectAsync(runner, userId, runnerConnection.HostedRunnerId, cancellationToken);
        await connectionSessionService.WriteApplicationEventsAsync(
            runner.Id,
            ApplicationEventLevels.Info,
            ApplicationEventCategories.Runtime,
            ApplicationEventTypes.RunnerConnected,
            "Runner connected.",
            new { runner.Name, runner.ConnectionId, runner.MachineName, runner.ApiInstanceId },
            userId,
            cancellationToken);
    }

    public async Task RecordHeartbeatAsync(
        Guid runnerId,
        Guid userId,
        RunnerDetails runnerHeartbeat,
        CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanUpdateRunnerAsync(runnerId, userId, cancellationToken))
            throw new UnauthorizedAccessException($"You do not have permission to update runner {runnerId}");

        var runner = await dbContext.Runners
            .SingleOrDefaultAsync(x => x.Id == runnerId, cancellationToken);
        if (runner == null)
            throw new InvalidOperationException($"Runner {runnerId} not found");

        // Ignore delayed heartbeats from an older SignalR connection after a replacement registers.
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
        ApplyInstallationReport(runner, runnerHeartbeat);

        if (string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase))
        {
            var host = await dbContext.RunnerHosts
                .SingleOrDefaultAsync(x => x.InfrastructureRunnerId == runner.Id, cancellationToken);
            if (host != null)
                host.LastHeartbeatAtUtc = runner.LastHeartbeatAtUtc;
        }

        dbContext.Entry(runner).State = EntityState.Modified;
        await dbContext.SaveChangesAsync(cancellationToken);
        await restartCompletionService.CompleteAsync(runner, cancellationToken);
    }

    public static void ApplyInstallationReport(Runner runner, RunnerDetails report)
    {
        // ApplicationProfile is intentionally absent here. Registration, reconnect, and heartbeat payloads
        // remain self-reports; the authoritative profile is assigned from server-controlled enrollment.
        runner.ImageDigest = TrimOrNull(report.ImageDigest, 256);
        runner.ContainerSpecificationVersion = report.ContainerSpecificationVersion is > 0 and <= 1000
            ? report.ContainerSpecificationVersion
            : null;
        runner.ApiCompatibilityVersion = TrimOrNull(report.ApiCompatibilityVersion, 40);
        runner.OperatingSystem = TrimOrNull(report.OperatingSystem, 256);
        runner.Architecture = TrimOrNull(report.Architecture, 40);
        runner.ContainerHealth = TrimOrNull(report.ContainerHealth, 40);

        if (RunnerPurposes.Normalize(runner.Purpose) == RunnerPurposes.Infrastructure
            && RunnerPurposes.Normalize(report.Purpose) == RunnerPurposes.Infrastructure)
        {
            runner.CapabilitiesJson = RunnerCapabilities.Serialize(report.Capabilities, RunnerPurposes.Infrastructure);
        }
    }

    private void ApplyConnectionDetails(Runner runner, RunnerDetails connection, DateTime now)
    {
        runner.ConnectionId = connection.ConnectionId;
        runner.LastConnectedAtUtc = now;
        runner.LastHeartbeatAtUtc = now;
        runner.Status = RunnerConnectivityStatuses.Online;
        runner.Version = connection.Version;
        runner.MachineName = connection.MachineName;
        runner.IpAddress = connection.IpAddress;
        runner.ApiInstanceId = connection.ApiInstanceId ?? apiInstanceIdentityService.ApiInstanceId;
        runner.ApiColor = connection.ApiColor ?? apiInstanceIdentityService.ApiColor;
        ApplyInstallationReport(runner, connection);
    }

    private async Task RecordRunnerConnectionSessionAsync(
        Runner runner,
        string connectionId,
        DateTime connectedAtUtc,
        CancellationToken cancellationToken)
    {
        dbContext.RunnerConnectionSessions.Add(new RunnerConnectionSession
        {
            Id = Guid.NewGuid(),
            RunnerId = runner.Id,
            ConnectionId = connectionId,
            ConnectedAtUtc = connectedAtUtc,
            ApiInstanceId = runner.ApiInstanceId,
            ApiColor = runner.ApiColor
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteHostedRunnerReconnectAsync(
        Runner runner,
        Guid userId,
        Guid? hostedRunnerId,
        CancellationToken cancellationToken)
    {
        await runnerRegistrationService.CompleteReconnectAsync(runner, userId, hostedRunnerId, cancellationToken);
        await managedRunnerAppAssignmentService.ReconcileHostedRunnerIdentityAsync(userId, runner.Id, cancellationToken);
        if (RunnerProfileRules.CanReceiveAutomaticApplicationAssignment(runner.Purpose, runner.ApplicationProfile))
            await managedRunnerAppAssignmentService.AssignEligibleAppsAsync(userId, runner.Id, cancellationToken);
    }

    private static string? TrimOrNull(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, maximumLength)];
    }
}
