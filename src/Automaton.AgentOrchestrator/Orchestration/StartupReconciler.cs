using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Publication;
using Automaton.AgentOrchestrator.Ci;
using Automaton.AgentOrchestrator.ReviewFeedback;
using Automaton.AgentOrchestrator.Workflows;
using Microsoft.Extensions.Logging;

namespace Automaton.AgentOrchestrator.Orchestration;

public sealed class StartupReconciler(
    IWorkspaceManager workspaces,
    IWorkTracker tracker,
    IExecutionFence executionFence,
    IWorkPublisher publisher,
    InMemoryOrchestrator orchestrator,
    OrchestratorOptions options,
    ILogger<StartupReconciler> logger,
    ICiShepherd? ciShepherd = null,
    IReviewFeedbackShepherd? reviewFeedbackShepherd = null,
    IReviewFeedbackProvider? reviewFeedbackProvider = null)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var repository = new WorkspaceRepository(options.Repository.Root, options.Repository.Identity, options.Repository.BaseBranch);
        var records = await workspaces.EnumerateOwnedWorkspacesAsync(repository, cancellationToken);
        var decisions = new List<Action>();

        foreach (var original in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await ResolveExecutionOwnershipAsync(original, cancellationToken);
            var item = await tracker.GetByIdAsync(record.WorkItemId, cancellationToken);
            logger.LogInformation("Reconciled workspace {WorkspaceKey} for work item {WorkItemId}: ownership {OwnershipState}, recovery {RecoveryState}, next attempt {NextAttempt}",
                record.Workspace.Key, record.WorkItemId, record.Workspace.ExecutionOwnershipStatus, record.Recovery.State, record.Recovery.NextAttempt);

            if (record.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined)
            {
                decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                    Math.Max(record.Recovery.LastCompletedAttempt, 1), "execution ownership is quarantined"));
                continue;
            }

            var eligible = item is not null && IsEligible(item);
            if (record.Recovery.State == WorkspaceRecoveryStates.Working)
            {
                if (!eligible)
                {
                    decisions.Add(() => orchestrator.RestoreIneligible(record.WorkItemId, record.Recovery.NextAttempt,
                        "work item is absent or no longer eligible during publication recovery"));
                    continue;
                }

                if (IsAwaitingHumanFeedbackRecovery(record))
                {
                    var targetIsValid = TryCreateAwaitingHumanFeedbackTarget(record, out var target, out var reason);
                    if (!options.ReviewFeedback.Enabled
                        || !targetIsValid
                        || reviewFeedbackProvider is null || reviewFeedbackShepherd is null)
                    {
                        decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                            Math.Max(record.Recovery.LastCompletedAttempt, 1), reason ?? "human feedback recovery is not configured"));
                        continue;
                    }

                    try
                    {
                        var snapshot = await reviewFeedbackProvider.GetAsync(target!, cancellationToken);
                        if (!string.Equals(snapshot.HeadSha, target!.HeadSha, StringComparison.OrdinalIgnoreCase)
                            || snapshot.Status is not (ReviewFeedbackStatuses.AwaitingHuman or ReviewFeedbackStatuses.Actionable or ReviewFeedbackStatuses.Approved))
                        {
                            decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                                Math.Max(record.Recovery.LastCompletedAttempt, 1), "review feedback provider returned an unexpected head SHA during recovery"));
                            continue;
                        }
                    }
                    catch (ReviewFeedbackProviderException exception) when (exception.IsRetryable)
                    {
                        throw;
                    }
                    catch (ReviewFeedbackProviderException exception)
                    {
                        decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                            Math.Max(record.Recovery.LastCompletedAttempt, 1), exception.Message));
                        continue;
                    }

                    var recoveredItem = item!;
                    var recoveredTarget = target!;
                    var attempt = Math.Max(record.Recovery.LastCompletedAttempt, 1);
                    decisions.Add(() => orchestrator.RestoreAwaitingHumanFeedback(recoveredItem, attempt, async token =>
                    {
                        var feedback = await reviewFeedbackShepherd.ShepherdAsync(recoveredTarget, record.Workspace, recoveredItem, token);
                        return feedback.Succeeded ? RunOutcome.Success() : RunOutcome.PermanentFailure(feedback.FailureReason ?? "human feedback remediation failed", feedback.FailureCategory);
                    }));
                    continue;
                }

                if (record.Publication?.State is not (null or PublicationStates.None))
                {
                    try
                    {
                        var recovered = await publisher.RecoverAsync(record, item!, cancellationToken);
                    if (recovered.Recognized)
                    {
                        var ci = recovered.Publication is not null && ciShepherd is not null
                            ? await ciShepherd.ShepherdAsync(new(recovered.Publication.Repository, recovered.Publication.PullRequestNumber, recovered.Publication.Branch, recovered.Publication.CommitSha), record.Workspace, item!, cancellationToken)
                            : CiShepherdResult.Success(recovered.Publication?.CommitSha ?? record.Publication?.CommitSha ?? string.Empty, record.Ci?.RemediationRound ?? 0);
                        var feedback = ci.Succeeded && recovered.Publication is not null && reviewFeedbackShepherd is not null
                            ? await reviewFeedbackShepherd.ShepherdAsync(new(recovered.Publication.Repository, recovered.Publication.PullRequestNumber, recovered.Publication.Branch, ci.HeadSha ?? recovered.Publication.CommitSha, record.ReviewFeedback?.RemediationRound ?? 0, record.Publication?.Revision ?? 1), record.Workspace, item!, cancellationToken)
                            : ReviewFeedbackResult.Success(ci.HeadSha ?? recovered.Publication?.CommitSha ?? string.Empty, record.ReviewFeedback?.RemediationRound ?? 0);
                        decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, ci.Succeeded && feedback.Succeeded ? RunStates.Completed : RunStates.Blocked,
                            Math.Max(record.Recovery.LastCompletedAttempt, 1), ci.FailureReason ?? feedback.FailureReason ?? "draft publication, CI, and human review were authoritatively recovered"));
                        continue;
                        }
                    }
                    catch (PublicationException exception) when (exception.IsRetryable)
                    {
                        throw;
                    }
                    catch (PublicationException exception)
                    {
                        decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                            Math.Max(record.Recovery.LastCompletedAttempt, 1), exception.Message));
                        continue;
                    }
                }
            }
            if (record.Recovery.State == WorkspaceRecoveryStates.Completed && record.Ci is not null && eligible && ciShepherd is not null
                && record.Publication is { State: PublicationStates.PullRequestCreated, CommitSha: not null, PullRequestNumber: not null })
            {
                var ci = await ciShepherd.ShepherdAsync(new(record.Workspace.RepositoryIdentity, record.Publication.PullRequestNumber.Value, record.Workspace.Branch, record.Publication.CommitSha), record.Workspace, item!, cancellationToken);
                var feedback = ci.Succeeded && reviewFeedbackShepherd is not null
                    ? await reviewFeedbackShepherd.ShepherdAsync(new(record.Workspace.RepositoryIdentity, record.Publication.PullRequestNumber.Value, record.Workspace.Branch, ci.HeadSha ?? record.Publication.CommitSha, record.ReviewFeedback?.RemediationRound ?? 0, record.Publication.Revision), record.Workspace, item!, cancellationToken)
                    : ReviewFeedbackResult.Success(ci.HeadSha ?? record.Publication.CommitSha, record.ReviewFeedback?.RemediationRound ?? 0);
                decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, ci.Succeeded && feedback.Succeeded ? RunStates.Completed : RunStates.Blocked,
                    Math.Max(record.Recovery.LastCompletedAttempt, 1), ci.FailureReason ?? feedback.FailureReason));
                continue;
            }
            switch (record.Recovery.State)
            {
                case WorkspaceRecoveryStates.Completed:
                    decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Completed,
                        record.Recovery.LastCompletedAttempt, record.Recovery.Reason));
                    break;
                case WorkspaceRecoveryStates.Blocked:
                    decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                        record.Recovery.LastCompletedAttempt, record.Recovery.Reason));
                    break;
                case WorkspaceRecoveryStates.Cancelled:
                    decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Cancelled,
                        record.Recovery.LastCompletedAttempt, record.Recovery.Reason));
                    break;
                case WorkspaceRecoveryStates.RetryableFailure when !eligible:
                    decisions.Add(() => orchestrator.RestoreIneligible(record.WorkItemId, record.Recovery.NextAttempt,
                        "work item is absent or no longer eligible"));
                    break;
                case WorkspaceRecoveryStates.RetryableFailure when record.Recovery.NextAttempt > options.Retry.MaxAttempts:
                    decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                        record.Recovery.LastCompletedAttempt, "retry attempts exhausted"));
                    break;
                case WorkspaceRecoveryStates.RetryableFailure:
                    var retryItem = item!;
                    decisions.Add(() => orchestrator.RestoreRetry(retryItem, record.Recovery.NextAttempt,
                        record.Recovery.RetryNotBeforeUtc, record.Recovery.Reason));
                    break;
                case WorkspaceRecoveryStates.Working:
                    decisions.Add(() => orchestrator.RestoreTerminalState(record.WorkItemId, RunStates.Blocked,
                        Math.Max(record.Recovery.LastCompletedAttempt, 1), "prior execution ended without a durable outcome"));
                    break;
                case WorkspaceRecoveryStates.Ineligible:
                    decisions.Add(() => orchestrator.RestoreIneligible(record.WorkItemId, record.Recovery.NextAttempt, record.Recovery.Reason));
                    break;
                case WorkspaceRecoveryStates.Ready when !eligible:
                    decisions.Add(() => orchestrator.RestoreIneligible(record.WorkItemId, record.Recovery.NextAttempt,
                        "work item is absent or no longer eligible"));
                    break;
            }
        }

        foreach (var decision in decisions) decision();
        orchestrator.CompleteStartupReconciliation();
        logger.LogInformation("Startup reconciliation completed for {WorkspaceCount} owned workspaces", records.Count);
    }

    private async Task<OwnedWorkspaceRecord> ResolveExecutionOwnershipAsync(OwnedWorkspaceRecord record, CancellationToken cancellationToken)
    {
        if (record.Workspace.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.Active) return record;
        if (record.ExecutionIdentity is null || !IsValidIdentity(record.ExecutionIdentity)
            || !string.Equals(record.ExecutionIdentity.LaunchNonce, record.Workspace.LaunchNonce, StringComparison.Ordinal))
            return await QuarantineAsync(record, "active execution has incomplete identity metadata", cancellationToken);

        var inspection = await executionFence.InspectAsync(record.ExecutionIdentity, cancellationToken);
        logger.LogInformation("Inspected execution ownership for work item {WorkItemId}, workspace {WorkspaceKey}: {ProcessIdentityClassification}",
            record.WorkItemId, record.Workspace.Key, inspection.Classification);
        if (inspection.Classification == ExecutionIdentityClassifications.Dead)
            return await ClearOwnershipAsync(record, cancellationToken);
        if (inspection.Classification == ExecutionIdentityClassifications.MatchingLive
            && await executionFence.TerminateAndVerifyAsync(record.ExecutionIdentity, TimeSpan.FromSeconds(10), cancellationToken))
            return await ClearOwnershipAsync(record, cancellationToken);

        return await QuarantineAsync(record, inspection.Detail ?? $"execution identity is {inspection.Classification}", cancellationToken);
    }

    private static bool IsValidIdentity(DurableExecutionIdentity identity)
        => identity.ProcessId > 0
           && !string.IsNullOrWhiteSpace(identity.LaunchNonce)
           && identity.FencingKind is ExecutionFencingKinds.WindowsJob or ExecutionFencingKinds.UnixProcessGroup or ExecutionFencingKinds.PortableProcessTree
           && (identity.FencingKind != ExecutionFencingKinds.UnixProcessGroup || identity.ProcessGroupId == identity.ProcessId);

    private async Task<OwnedWorkspaceRecord> ClearOwnershipAsync(OwnedWorkspaceRecord record, CancellationToken cancellationToken)
    {
        var workspace = await workspaces.UpdateExecutionOwnershipAsync(record.Workspace, WorkspaceExecutionOwnershipStates.None,
            record.Workspace.LaunchNonce!, cancellationToken);
        return record with { Workspace = workspace, ExecutionIdentity = null };
    }

    private async Task<OwnedWorkspaceRecord> QuarantineAsync(OwnedWorkspaceRecord record, string reason, CancellationToken cancellationToken)
    {
        logger.LogWarning("Quarantining workspace {WorkspaceKey}: {Reason}", record.Workspace.Key, reason);
        var workspace = await workspaces.UpdateExecutionOwnershipAsync(record.Workspace, WorkspaceExecutionOwnershipStates.Quarantined,
            record.Workspace.LaunchNonce ?? record.ExecutionIdentity?.LaunchNonce ?? "unknown", cancellationToken);
        return record with { Workspace = workspace };
    }

    private bool IsEligible(WorkItem item)
        => options.Tracker.ActiveProviderLifecycleStates.Contains(item.ProviderLifecycleState, StringComparer.OrdinalIgnoreCase)
           && options.Tracker.RequiredLabels.All(required => item.Labels.Contains(required, StringComparer.OrdinalIgnoreCase));

    private static bool IsAwaitingHumanFeedbackRecovery(OwnedWorkspaceRecord record)
        => record.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.None
           && record.Publication?.State == PublicationStates.PullRequestCreated
           && record.Ci?.Status == CiStatuses.Passed
           && record.ReviewFeedback?.Status is ReviewFeedbackStatuses.AwaitingHuman or ReviewFeedbackStatuses.Actionable;

    private static bool TryCreateAwaitingHumanFeedbackTarget(OwnedWorkspaceRecord record, out ReviewFeedbackTarget? target, out string? reason)
    {
        target = null;
        reason = null;
        var publication = record.Publication;
        var ci = record.Ci;
        var feedback = record.ReviewFeedback;
        if (publication is null || ci is null || feedback is null
            || publication.PullRequestNumber is not > 0
            || string.IsNullOrWhiteSpace(publication.PullRequestUrl)
            || !Uri.TryCreate(publication.PullRequestUrl, UriKind.Absolute, out _)
            || !IsSha(publication.CommitSha)
            || !IsSha(ci.HeadSha)
            || !IsSha(feedback.HeadSha)
            || !string.Equals(ci.HeadSha, publication.CommitSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(feedback.HeadSha, publication.CommitSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(publication.RemoteBranch, record.Workspace.Branch, StringComparison.Ordinal)
            || publication.Revision < 1
            || ci.RemediationRound < 0
            || feedback.RemediationRound < 0)
        {
            reason = "durable published draft state is inconsistent for human feedback recovery";
            return false;
        }

        target = new ReviewFeedbackTarget(record.Workspace.RepositoryIdentity, publication.PullRequestNumber.Value,
            record.Workspace.Branch, publication.CommitSha!, feedback.RemediationRound, publication.Revision);
        return true;
    }

    private static bool IsSha(string? value)
        => value is { Length: 40 } && value.All(Uri.IsHexDigit);
}
