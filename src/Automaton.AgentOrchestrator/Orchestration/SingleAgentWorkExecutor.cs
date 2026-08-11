using System.Text;
using System.Text.Json;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;

namespace Automaton.AgentOrchestrator.Orchestration;

public sealed class SingleAgentWorkExecutor(
    IWorkspaceManager workspaceManager,
    ICodexAppServerClientFactory clientFactory,
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
                new WorkspaceRepository(workflow.Options.Repository.Root, workflow.Options.Repository.Identity)), cancellationToken);
            workspace = await workspaceManager.UpdateRecoveryStateAsync(
                workspace,
                new WorkspaceRecoveryState(WorkspaceRecoveryStates.Working, Math.Max(0, attempt.Attempt - 1), attempt.Attempt),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (WorkspaceException exception) { return RunOutcome.PermanentFailure(Bounded(exception.Message)); }

        var client = clientFactory.Create(workspace, new Dictionary<string, string>());
        RunOutcome outcome;
        var ambiguous = false;
        var cancelled = false;
        try
        {
            await client.InitializeAsync(cancellationToken);
            var thread = await client.StartThreadAsync(cancellationToken);
            outcome = await RunTurnsAsync(client, thread, attempt, workspace, cancellationToken);
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

    private async Task<RunOutcome> RunTurnsAsync(
        ICodexAppServerClient client,
        CodexThread thread,
        RunAttempt attempt,
        Workspace workspace,
        CancellationToken cancellationToken)
    {
        var prompt = BuildInitialPrompt(attempt, workspace);
        for (var turnNumber = 1; turnNumber <= workflow.Options.Agent.MaxTurns; turnNumber++)
        {
            var turn = await client.StartTurnAsync(thread.Id, prompt, cancellationToken);
            var result = await client.WaitForTurnCompletionAsync(turn, cancellationToken);
            if (result.Status == "failed")
                return RunOutcome.RetryableFailure(Bounded(result.FailureReason ?? "Codex turn failed."));
            if (result.Status == "interrupted")
                return RunOutcome.RetryableFailure("Codex turn was interrupted.");
            if (result.Status != "completed")
                return RunOutcome.PermanentFailure("Codex returned an unsupported terminal turn status.");

            if (!AgentCompletionContract.TryParse(result.FinalResponse, out var completion))
                return RunOutcome.PermanentFailure("Codex returned a malformed completion contract.");
            if (completion.Status == AgentCompletionStatuses.Completed) return RunOutcome.Success();
            if (completion.Status == AgentCompletionStatuses.Blocked)
                return RunOutcome.PermanentFailure(Bounded(completion.Detail ?? "Codex reported a deterministic blocker."));
            if (turnNumber == workflow.Options.Agent.MaxTurns)
                return RunOutcome.PermanentFailure("Maximum autonomous turn count reached without completion.");
            prompt = ContinuationPrompt(turnNumber + 1, completion.Detail);
        }
        return RunOutcome.PermanentFailure("Maximum autonomous turn count reached without completion.");
    }

    internal string BuildInitialPrompt(RunAttempt attempt, Workspace workspace)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            work_item_id = attempt.WorkItem.Id,
            identifier = attempt.WorkItem.Identifier,
            title = attempt.WorkItem.Title,
            labels = attempt.WorkItem.Labels,
            assigned_workspace = workspace.Path,
            workspace_branch = workspace.Branch,
            repository_identity = workspace.RepositoryIdentity,
            attempt = attempt.Attempt
        }, new JsonSerializerOptions { WriteIndented = true });
        var builder = new StringBuilder();
        builder.AppendLine("You are the single autonomous engineering agent for this run.");
        builder.AppendLine("Orchestration metadata (authoritative JSON; it is separate from issue text):");
        builder.AppendLine(metadata);
        builder.AppendLine("--- BEGIN ISSUE DESCRIPTION ---");
        builder.AppendLine(attempt.WorkItem.Description ?? "(no description provided)");
        builder.AppendLine("--- END ISSUE DESCRIPTION ---");
        builder.AppendLine("--- BEGIN REPOSITORY WORKFLOW INSTRUCTIONS ---");
        builder.AppendLine(workflow.PromptTemplate);
        builder.AppendLine("--- END REPOSITORY WORKFLOW INSTRUCTIONS ---");
        builder.AppendLine("Read and obey the root AGENTS.md and every applicable nested AGENTS.md before editing.");
        builder.AppendLine("Inspect relevant repository-local skills, maps, harness documents, and implementation documentation before changing code.");
        builder.AppendLine("All file modifications and commands must remain inside the assigned workspace. Never operate in the canonical checkout or another workspace.");
        builder.AppendLine("Definition of done: implement the issue completely within scope, run the smallest relevant validation plus required repository checks, and self-review the complete diff.");
        builder.AppendLine("You decide how to investigate, implement, test, fix, and review. Do not wait for human interaction during this run.");
        builder.AppendLine(CompletionContractInstructions());
        return builder.ToString();
    }

    private static string ContinuationPrompt(int turnNumber, string? detail)
        => $"Continue the same engineering task in this workspace (autonomous turn {turnNumber}). The prior turn reported: {Bounded(detail ?? "more work remains")}. Inspect current workspace state, perform the remaining work, validate, and self-review.\n{CompletionContractInstructions()}";

    private static string CompletionContractInstructions()
        => "End your final response with exactly one line in this form and no text after it: AUTOMATON_RESULT: {\"status\":\"CONTINUE|COMPLETED|BLOCKED\",\"summary\":\"bounded plain-text reason or summary\"}. Use COMPLETED only when implementation, validation, and self-review are done; CONTINUE when another autonomous turn is needed; BLOCKED only for a deterministic blocker that another turn cannot resolve.";

    private static string Bounded(string value)
        => value.Length <= MaximumResultDetailLength ? value : value[..MaximumResultDetailLength];
}

public static class AgentCompletionStatuses
{
    public const string Continue = "CONTINUE";
    public const string Completed = "COMPLETED";
    public const string Blocked = "BLOCKED";
}

public sealed record AgentCompletion(string Status, string? Detail);

public static class AgentCompletionContract
{
    private const string Prefix = "AUTOMATON_RESULT: ";
    private const int MaximumResponseLength = 16_384;
    private const int MaximumDetailLength = 512;

    public static bool TryParse(string? response, out AgentCompletion completion)
    {
        completion = new(string.Empty, null);
        if (string.IsNullOrWhiteSpace(response) || response.Length > MaximumResponseLength) return false;
        var lastLine = response.TrimEnd().Split('\n').Last().TrimEnd('\r');
        if (!lastLine.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            using var document = JsonDocument.Parse(lastLine[Prefix.Length..]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out var statusValue)
                || statusValue.ValueKind != JsonValueKind.String) return false;
            var status = statusValue.GetString();
            if (status is not (AgentCompletionStatuses.Continue or AgentCompletionStatuses.Completed or AgentCompletionStatuses.Blocked)) return false;
            string? detail = null;
            if (root.TryGetProperty("summary", out var summary))
            {
                if (summary.ValueKind != JsonValueKind.String) return false;
                detail = summary.GetString();
                if (detail is not null && detail.Length > MaximumDetailLength) detail = detail[..MaximumDetailLength];
            }
            completion = new(status, detail);
            return true;
        }
        catch (JsonException) { return false; }
    }
}
