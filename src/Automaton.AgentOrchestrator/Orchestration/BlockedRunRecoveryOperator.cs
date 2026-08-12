using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;

namespace Automaton.AgentOrchestrator.Orchestration;

public sealed record OperatorRetryResult(bool Accepted, string Category, string Message, int? Attempt = null);

public interface IBlockedRunRecoveryOperator
{
    Task<OperatorRetryResult> RequestRetryAsync(string workItemId, CancellationToken cancellationToken);
}

public sealed class BlockedRunRecoveryOperator(
    IWorkspaceManager workspaces,
    IGitCommandRunner git,
    IWorkTracker tracker,
    OrchestratorOptions options,
    IOperatorTelemetry telemetry) : IBlockedRunRecoveryOperator
{
    public async Task<OperatorRetryResult> RequestRetryAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workItemId)) return Reject("invalid-work-item", "work item ID is required");
        var repository = new WorkspaceRepository(options.Repository.Root, options.Repository.Identity, options.Repository.BaseBranch);
        var record = (await workspaces.EnumerateOwnedWorkspacesAsync(repository, cancellationToken))
            .SingleOrDefault(value => string.Equals(value.WorkItemId, workItemId, StringComparison.Ordinal));
        if (record is null) return Reject("not-found", "owned workspace was not found", workItemId);
        if (record.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined)
            return Reject("quarantined", "quarantined ownership cannot be retried", workItemId);
        if (record.Workspace.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.None)
            return Reject("active-ownership", "execution ownership is not released", workItemId);
        if (record.Recovery.State != WorkspaceRecoveryStates.Blocked)
            return Reject("not-blocked", "run is not durably blocked", workItemId);
        var item = await tracker.GetByIdAsync(workItemId, cancellationToken);
        if (item is null || !IsEligible(item)) return Reject("ineligible", "work item is absent or no longer eligible", workItemId);
        if (record.Publication is null || record.Publication.State == PublicationStates.None)
            return Reject("missing-publication-authorization", "durable publication authorization is missing", workItemId);
        if (record.Publication.State == PublicationStates.Authorized && string.IsNullOrWhiteSpace(record.Publication.AuthorizedParentSha))
            return Reject("publication-integrity-failure", "authorized publication provenance is incomplete", workItemId);
        if (!PathWithin(options.Workspace.Root, record.Workspace.Path) || PathsEqual(record.Workspace.Path, options.Repository.Root))
            return Reject("workspace-integrity-failure", "workspace path is outside the configured workspace boundary", workItemId);
        bool registered;
        try { registered = await IsRegisteredWorktreeAsync(record.Workspace, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { registered = false; }
        if (!registered)
            return Reject("workspace-integrity-failure", "workspace registration or branch is invalid", workItemId);
        var nextAttempt = Math.Max(record.Recovery.NextAttempt, record.Recovery.LastCompletedAttempt + 1);
        Workspace updated;
        try
        {
            updated = await workspaces.TransitionRecoveryStateAsync(record.Workspace, WorkspaceRecoveryStates.Blocked,
                record.Publication.State,
                new WorkspaceRecoveryState(WorkspaceRecoveryStates.Working, record.Recovery.LastCompletedAttempt, nextAttempt, Reason: "operator retry requested"), cancellationToken);
        }
        catch (WorkspaceRecoveryException)
        {
            return Reject("transition-conflict", "workspace recovery state changed before authorization", workItemId);
        }
        telemetry.Record("run.operator_retry_requested", OperatorEventSeverities.Warning, workItemId, "recovery", message: "Blocked publication recovery was explicitly authorized.", metadata: new Dictionary<string, string>
        {
            ["workspaceKey"] = updated.Key, ["previousState"] = WorkspaceRecoveryStates.Blocked, ["newState"] = WorkspaceRecoveryStates.Working,
            ["publicationState"] = record.Publication.State, ["attempt"] = nextAttempt.ToString(System.Globalization.CultureInfo.InvariantCulture), ["category"] = "operator-requested-publication-recovery"
        });
        return new(true, "accepted", "publication recovery requested", nextAttempt);
    }

    private OperatorRetryResult Reject(string category, string message, string? workItemId = null)
    {
        telemetry.Record("run.operator_retry_rejected", OperatorEventSeverities.Warning, workItemId, "recovery", message: message, metadata: new Dictionary<string, string> { ["category"] = category });
        return new(false, category, message);
    }

    private bool IsEligible(WorkItem item) => options.Tracker.ActiveProviderLifecycleStates.Contains(item.ProviderLifecycleState, StringComparer.OrdinalIgnoreCase)
        && options.Tracker.RequiredLabels.All(label => item.Labels.Contains(label, StringComparer.OrdinalIgnoreCase));

    private async Task<bool> IsRegisteredWorktreeAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(options.Repository.Root, ["worktree", "list", "--porcelain"], cancellationToken);
        if (result.ExitCode != 0) return false;
        string? path = null;
        string? branch = null;
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                path = line["worktree ".Length..];
                branch = null;
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal) && path is not null)
            {
                branch = line["branch ".Length..];
                if (PathsEqual(path, workspace.Path))
                    return string.Equals(branch, $"refs/heads/{workspace.Branch}", StringComparison.Ordinal);
            }
        }
        return false;
    }
    private static bool PathWithin(string root, string path)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(r, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
