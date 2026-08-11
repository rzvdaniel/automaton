using System.Runtime.ExceptionServices;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;
using Automaton.AgentOrchestrator.Publication;

namespace Automaton.AgentOrchestrator.Orchestration;

public sealed class RoleBasedWorkExecutor(
    IWorkspaceManager workspaceManager,
    IWorkspaceMutationGuard mutationGuard,
    ICodexAppServerClientFactory clientFactory,
    IWorkPublisher publisher,
    WorkflowDefinition workflow) : IWorkExecutor
{
    private const int MaximumResultDetailLength = 512;

    public async Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken)
    {
        Workspace workspace;
        try
        {
            workspace = await workspaceManager.CreateOrRecoverAsync(new(
                attempt.WorkItem,
                new WorkspaceRepository(workflow.Options.Repository.Root, workflow.Options.Repository.Identity, workflow.Options.Repository.BaseBranch)), cancellationToken);
            workspace = await workspaceManager.UpdateRecoveryStateAsync(
                workspace,
                new WorkspaceRecoveryState(WorkspaceRecoveryStates.Working, Math.Max(0, attempt.Attempt - 1), attempt.Attempt),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkspaceException exception) { return RunOutcome.PermanentFailure(Bounded(exception.Message)); }

        var client = clientFactory.Create(workspace, new Dictionary<string, string>());
        var prompts = new RolePromptBuilder(workflow);
        RunOutcome outcome;
        var ambiguous = false;
        var cancelled = false;
        try
        {
            await client.InitializeAsync(cancellationToken);

            var manager = await RunReadOnlyRoleAsync(client, workspace, AgentRoles.Manager, async () =>
            {
                var thread = await client.StartThreadAsync(cancellationToken);
                return await RunManagerAsync(client, thread, prompts.Manager(attempt, workspace), cancellationToken);
            }, cancellationToken);
            if (manager.Status == RoleResultStatuses.Blocked) throw new RoleBlockedException(manager.Summary);

            var developerThread = await client.StartThreadAsync(cancellationToken);
            var developer = await RunDeveloperAsync(client, developerThread, prompts.Developer(attempt, workspace, manager), cancellationToken);
            if (developer.Status == RoleResultStatuses.Blocked) throw new RoleBlockedException(developer.Summary);

            var reviewerThread = await client.StartThreadAsync(cancellationToken);
            var reviewerPrompt = prompts.Reviewer(attempt, workspace, manager, developer);
            ReviewerHandoff? approvedReview = null;
            outcome = RunOutcome.PermanentFailure("Maximum review rounds reached without approval.");
            for (var reviewRound = 1; reviewRound <= workflow.Options.Agent.MaxReviewRounds; reviewRound++)
            {
                var reviewer = await RunReadOnlyRoleAsync(client, workspace, AgentRoles.Reviewer,
                    () => RunReviewerAsync(client, reviewerThread, reviewerPrompt, cancellationToken), cancellationToken);
                if (reviewer.Verdict == RoleResultStatuses.Approved)
                {
                    outcome = RunOutcome.Success();
                    approvedReview = reviewer;
                    break;
                }
                if (reviewer.Verdict == RoleResultStatuses.Blocked)
                    throw new RoleBlockedException(reviewer.Summary.Length > 0 ? reviewer.Summary : "Reviewer could not complete a trustworthy review.");
                if (reviewRound == workflow.Options.Agent.MaxReviewRounds)
                    throw new RoleBlockedException("Maximum review rounds reached without approval.");

                developer = await RunDeveloperAsync(
                    client,
                    developerThread,
                    prompts.DeveloperRemediation(attempt, workspace, manager, developer, reviewer, reviewRound + 1),
                    cancellationToken);
                if (developer.Status == RoleResultStatuses.Blocked) throw new RoleBlockedException(developer.Summary);
                reviewerPrompt = prompts.ReviewerAgain(attempt, workspace, manager, developer, reviewer, reviewRound + 1);
            }
            if (approvedReview is null) throw new RoleBlockedException("Reviewer approval was not established for publication.");
            await publisher.PublishAsync(new(client.CurrentWorkspace ?? workspace, attempt.WorkItem, manager, developer, approvedReview), cancellationToken);
        }
        catch (RoleBlockedException exception) { outcome = RunOutcome.PermanentFailure(Bounded(exception.Message)); }
        catch (RoleInfrastructureException exception) { outcome = RunOutcome.RetryableFailure(Bounded(exception.Message)); }
        catch (PublicationException exception)
        {
            outcome = exception.IsRetryable
                ? RunOutcome.RetryableFailure(Bounded(exception.Message))
                : RunOutcome.PermanentFailure(Bounded(exception.Message));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            outcome = RunOutcome.PermanentFailure("Execution was cancelled.");
        }
        catch (CodexIdleTimeoutException exception) { outcome = RunOutcome.RetryableFailure(Bounded(exception.Message)); }
        catch (CodexProcessExitedException exception) { ambiguous = true; outcome = RunOutcome.PermanentFailure(Bounded(exception.Message)); }
        catch (CodexProtocolException exception) { ambiguous = true; outcome = RunOutcome.PermanentFailure(Bounded(exception.Message)); }
        catch (Exception exception)
        {
            ambiguous = string.Equals(client.CurrentWorkspace?.ExecutionOwnershipStatus, WorkspaceExecutionOwnershipStates.Quarantined, StringComparison.Ordinal);
            outcome = ambiguous
                ? RunOutcome.PermanentFailure("Codex execution ownership is quarantined for manual recovery.")
                : RunOutcome.RetryableFailure(Bounded(exception.Message));
        }

        try { await client.DisposeAsync(); }
        catch (Exception exception)
        {
            ambiguous = true;
            outcome = RunOutcome.PermanentFailure(Bounded($"Codex shutdown could not verify ownership release: {exception.Message}"));
        }

        var currentWorkspace = client.CurrentWorkspace ?? workspace;
        if (ambiguous || currentWorkspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined) return outcome;

        var recovery = cancelled
            ? new WorkspaceRecoveryState(WorkspaceRecoveryStates.Cancelled, attempt.Attempt, attempt.Attempt + 1)
            : outcome.Succeeded
                ? new WorkspaceRecoveryState(WorkspaceRecoveryStates.Completed, attempt.Attempt, attempt.Attempt + 1)
                : outcome.IsRetryable
                    ? new WorkspaceRecoveryState(WorkspaceRecoveryStates.RetryableFailure, attempt.Attempt, attempt.Attempt + 1, Reason: outcome.FailureReason)
                    : new WorkspaceRecoveryState(WorkspaceRecoveryStates.Blocked, attempt.Attempt, attempt.Attempt + 1, Reason: outcome.FailureReason);
        try { currentWorkspace = await workspaceManager.UpdateRecoveryStateAsync(currentWorkspace, recovery, CancellationToken.None); }
        catch (WorkspaceException exception) { return RunOutcome.PermanentFailure(Bounded(exception.Message)); }
        if (cancelled) throw new OperationCanceledException(cancellationToken);

        var lifecycle = outcome.Succeeded
            ? WorkspaceLifecycleStates.Ready
            : outcome.IsRetryable
                ? WorkspaceLifecycleStates.RetryableFailure
                : WorkspaceLifecycleStates.Failed;
        try { await workspaceManager.UpdateLifecycleAsync(currentWorkspace, lifecycle, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkspaceException exception) { return RunOutcome.PermanentFailure(Bounded(exception.Message)); }
        return outcome;
    }

    private async Task<ManagerHandoff> RunManagerAsync(ICodexAppServerClient client, CodexThread thread, string prompt, CancellationToken cancellationToken)
    {
        for (var turn = 1; turn <= workflow.Options.Agent.ManagerMaxTurns; turn++)
        {
            var terminal = await RunTurnAsync(client, thread, prompt, cancellationToken);
            if (!RoleResultContracts.TryParseManager(terminal.FinalResponse, out var result))
                throw new RoleBlockedException("Manager returned a malformed terminal contract.");
            if (result.Status != RoleResultStatuses.Continue) return result;
            prompt = RolePromptBuilder.Continue(AgentRoles.Manager, result.Summary);
        }
        throw new RoleBlockedException("Manager exceeded its autonomous turn limit.");
    }

    private async Task<DeveloperHandoff> RunDeveloperAsync(ICodexAppServerClient client, CodexThread thread, string prompt, CancellationToken cancellationToken)
    {
        for (var turn = 1; turn <= workflow.Options.Agent.DeveloperMaxTurns; turn++)
        {
            var terminal = await RunTurnAsync(client, thread, prompt, cancellationToken);
            if (!RoleResultContracts.TryParseDeveloper(terminal.FinalResponse, out var result))
                throw new RoleBlockedException("Developer returned a malformed terminal contract.");
            if (result.Status != RoleResultStatuses.Continue) return result;
            prompt = RolePromptBuilder.Continue(AgentRoles.Developer, result.Summary);
        }
        throw new RoleBlockedException("Developer exceeded its autonomous turn limit.");
    }

    private async Task<ReviewerHandoff> RunReviewerAsync(ICodexAppServerClient client, CodexThread thread, string prompt, CancellationToken cancellationToken)
    {
        for (var turn = 1; turn <= workflow.Options.Agent.ReviewerMaxTurns; turn++)
        {
            var terminal = await RunTurnAsync(client, thread, prompt, cancellationToken);
            if (!RoleResultContracts.TryParseReviewer(terminal.FinalResponse, out var result))
                throw new RoleBlockedException("Reviewer returned a malformed terminal contract.");
            if (result.Verdict != RoleResultStatuses.Continue) return result;
            prompt = RolePromptBuilder.Continue(AgentRoles.Reviewer, result.Summary);
        }
        throw new RoleBlockedException("Reviewer exceeded its autonomous turn limit.");
    }

    private static async Task<CodexTurnResult> RunTurnAsync(ICodexAppServerClient client, CodexThread thread, string prompt, CancellationToken cancellationToken)
    {
        var turn = await client.StartTurnAsync(thread.Id, prompt, cancellationToken);
        var result = await client.WaitForTurnCompletionAsync(turn, cancellationToken);
        if (result.Status == "failed") throw new RoleInfrastructureException(result.FailureReason ?? "Codex role turn failed.");
        if (result.Status == "interrupted") throw new RoleInfrastructureException("Codex role turn was interrupted.");
        if (result.Status != "completed") throw new RoleBlockedException("Codex returned an unsupported terminal turn status.");
        return result;
    }

    private async Task<T> RunReadOnlyRoleAsync<T>(
        ICodexAppServerClient client,
        Workspace workspace,
        string role,
        Func<Task<T>> execute,
        CancellationToken cancellationToken)
    {
        var before = await mutationGuard.CaptureAsync(workspace, cancellationToken);
        T? result = default;
        Exception? failure = null;
        try { result = await execute(); }
        catch (Exception exception) { failure = exception; }

        if (failure is CodexProtocolException
            || string.Equals(client.CurrentWorkspace?.ExecutionOwnershipStatus, WorkspaceExecutionOwnershipStates.Quarantined, StringComparison.Ordinal))
        {
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            throw new CodexProtocolException($"{role} execution ownership became quarantined.");
        }

        string after;
        try { after = await mutationGuard.CaptureAsync(workspace, CancellationToken.None); }
        catch (Exception exception)
        {
            throw new RoleBlockedException($"{role} workspace could not be safely verified after execution: {Bounded(exception.Message)}");
        }
        if (!string.Equals(before, after, StringComparison.Ordinal))
            throw new RoleBlockedException($"{role} violated its read-only workspace boundary.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    private static string Bounded(string value) => value.Length <= MaximumResultDetailLength ? value : value[..MaximumResultDetailLength];
    private sealed class RoleBlockedException(string message) : Exception(message);
    private sealed class RoleInfrastructureException(string message) : Exception(message);
}
