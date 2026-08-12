using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Hosting;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.AgentOrchestrator.Operator;

public sealed class OperatorStatusHostedService(
    InMemoryOrchestrator orchestrator,
    IWorkspaceManager workspaces,
    IOperatorTelemetry telemetry,
    IClock clock,
    OrchestratorOptions options,
    ILogger<OperatorStatusHostedService> logger,
    IBuildIdentity? buildIdentity = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Observability.Enabled || telemetry is not FileOperatorTelemetry files) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Observability.SnapshotIntervalMilliseconds));
        do { await WriteAsync(files, stoppingToken); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal Task WriteOnceAsync(CancellationToken cancellationToken)
        => telemetry is FileOperatorTelemetry files ? WriteAsync(files, cancellationToken) : Task.CompletedTask;

    private async Task WriteAsync(FileOperatorTelemetry files, CancellationToken cancellationToken)
    {
        IReadOnlyList<OwnedWorkspaceRecord> records = [];
        var healthy = true;
        string? error = null;
        try
        {
            records = await workspaces.EnumerateOwnedWorkspacesAsync(new(options.Repository.Root, options.Repository.Identity, options.Repository.BaseBranch), cancellationToken);
            if (orchestrator.IsStartupReconciled && telemetry.DaemonState().State is OperatorDaemonStates.Starting or OperatorDaemonStates.Reconciling)
                telemetry.SetDaemonState(OperatorDaemonStates.Running);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            healthy = false;
            error = "operator snapshot could not enumerate durable workspace state";
            telemetry.SetDaemonState(OperatorDaemonStates.Degraded, error);
            telemetry.Record("operator.snapshot.failed", OperatorEventSeverities.Warning, message: error);
            logger.LogWarning(exception, "Operator snapshot could not enumerate durable workspace state");
        }

        var durable = records.ToDictionary(record => record.WorkItemId, StringComparer.Ordinal);
        var runs = orchestrator.Snapshot().GroupBy(run => run.WorkItemId, StringComparer.Ordinal).Select(group => group.OrderByDescending(run => run.UpdatedAtUtc).First()).Select(run =>
        {
            durable.TryGetValue(run.WorkItemId, out var record);
            var latest = telemetry.LatestFor(run.WorkItemId);
            return new OperatorRunStatus(run.WorkItemId, run.State, run.Attempt, run.UpdatedAtUtc, latest?.Stage, latest?.Role,
                record?.Workspace.Key, record?.Workspace.Path, record?.Workspace.Branch, record?.Publication?.PullRequestUrl,
                record?.Publication?.CommitSha, record?.Ci?.Status, record?.ReviewFeedback?.Status, record?.Workspace.ExecutionOwnershipStatus,
                run.RetryAtUtc, FileOperatorTelemetry.SanitizeDiagnostic(run.FailureReason ?? record?.Recovery.Reason), record?.Ci?.LastObservedAtUtc, record?.ReviewFeedback?.LastObservedAtUtc);
        }).OrderBy(run => run.WorkItemId, StringComparer.Ordinal).ToArray();
        var daemon = telemetry.DaemonState();
        try
        {
            var build = buildIdentity?.Current ?? new BuildIdentity("unknown", "unknown");
            files.WriteSnapshot(new(1, clock.UtcNow, daemon.State, orchestrator.IsStartupReconciled, healthy && daemon.State != OperatorDaemonStates.Degraded, options.Repository.Identity, runs, error ?? daemon.LastError, build.Version, build.Revision));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            telemetry.SetDaemonState(OperatorDaemonStates.Degraded, "operator status persistence failed");
            logger.LogWarning(exception, "Operator status persistence failed");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        telemetry.SetDaemonState(OperatorDaemonStates.Stopping);
        telemetry.Record("daemon.stopping", OperatorEventSeverities.Information, message: "Agent orchestrator is stopping.");
        await base.StopAsync(cancellationToken);
    }

}
