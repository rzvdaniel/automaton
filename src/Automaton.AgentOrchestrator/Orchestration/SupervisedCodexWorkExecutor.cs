using System.Text.Json;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;

namespace Automaton.AgentOrchestrator.Orchestration;

public sealed record AgentTerminalResult(string Status, string Summary);

public static class AgentTerminalContract
{
    public const string Instruction = "End with exactly one terminal line: AUTOMATON_RESULT: {\"status\":\"CONTINUE|COMPLETED|BLOCKED|RETRYABLE\",\"summary\":\"bounded text\"}";

    public static bool TryParse(string? response, out AgentTerminalResult result)
    {
        result = new(string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(response) || response.Length > 16_384) return false;
        var line = response.TrimEnd().Split('\n').Last().TrimEnd('\r');
        const string prefix = "AUTOMATON_RESULT: ";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
        try
        {
            using var document = JsonDocument.Parse(line[prefix.Length..]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("status", out var statusValue)
                || statusValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("summary", out var summaryValue)
                || summaryValue.ValueKind != JsonValueKind.String) return false;
            var status = statusValue.GetString() ?? string.Empty;
            var summary = summaryValue.GetString() ?? string.Empty;
            if (summary.Length > 512 || status is not ("CONTINUE" or "COMPLETED" or "BLOCKED" or "RETRYABLE")) return false;
            result = new(status, summary);
            return true;
        }
        catch (JsonException) { return false; }
    }
}

public sealed class SupervisedCodexWorkExecutor(
    IWorkspaceManager workspaceManager,
    ICodexAppServerClientFactory clients,
    WorkflowDefinition workflow,
    IOperatorTelemetry? telemetry = null) : IWorkExecutor
{
    public async Task<RunOutcome> ExecuteAsync(RunAttempt attempt, CancellationToken cancellationToken)
    {
        WorkspaceExecutionContext context;
        try
        {
            var workspace = await workspaceManager.CreateOrRecoverAsync(new(attempt.WorkItem,
                new WorkspaceRepository(workflow.Options.Repository.Root, workflow.Options.Repository.Identity, workflow.Options.Repository.BaseBranch)), cancellationToken);
            context = new WorkspaceExecutionContext(workspaceManager, workspace);
            await context.SetRecoveryAsync(new WorkspaceRecoveryState(WorkspaceRecoveryStates.Working, Math.Max(0, attempt.Attempt - 1), attempt.Attempt), cancellationToken);
        }
        catch (WorkspaceException exception) { return RunOutcome.PermanentFailure(Bound(exception.Message)); }

        var capability = CodexRuntimeCapability.From(workflow.Options.Agent);
        var client = clients.Create(context, capability.ChildEnvironment);
        RunOutcome outcome;
        var quarantined = false;
        try
        {
            await client.InitializeAsync(cancellationToken);
            var thread = await client.StartThreadAsync(cancellationToken);
            var prompt = BuildPrompt(attempt, context.Workspace, capability);
            for (var turnIndex = 0; turnIndex < workflow.Options.Agent.MaxTurns; turnIndex++)
            {
                var turn = await client.StartTurnAsync(thread.Id, prompt, cancellationToken);
                var result = await client.WaitForTurnCompletionAsync(turn, cancellationToken);
                if (!string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Bound(result.FailureReason ?? "Codex task turn did not complete."));
                if (!AgentTerminalContract.TryParse(result.FinalResponse, out var terminal))
                    return await FinishAsync(context, client, attempt, RunOutcome.PermanentFailure("Codex returned a malformed task terminal contract."), CancellationToken.None);
                if (terminal.Status == "COMPLETED") return await FinishAsync(context, client, attempt, RunOutcome.Success(), CancellationToken.None);
                if (terminal.Status == "BLOCKED") return await FinishAsync(context, client, attempt, RunOutcome.PermanentFailure(Bound(terminal.Summary)), CancellationToken.None);
                if (terminal.Status == "RETRYABLE") return await FinishAsync(context, client, attempt, RunOutcome.RetryableFailure(Bound(terminal.Summary)), CancellationToken.None);
                if (turnIndex + 1 == workflow.Options.Agent.MaxTurns)
                    return await FinishAsync(context, client, attempt, RunOutcome.PermanentFailure("Codex exceeded the configured task turn limit."), CancellationToken.None);
                prompt = "Continue the assigned task in the same workspace. " + AgentTerminalContract.Instruction;
            }
            return await FinishAsync(context, client, attempt, RunOutcome.PermanentFailure("Codex exceeded the configured task turn limit."), CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { await client.DisposeAsync(); }
            catch { /* Disposal preserves its own release-or-quarantine fencing outcome. */ }
            throw;
        }
        catch (CodexIdleTimeoutException exception) { outcome = RunOutcome.RetryableFailure(Bound(exception.Message)); }
        catch (CodexTurnTimeoutException exception) { outcome = RunOutcome.RetryableFailure(Bound(exception.Message)); }
        catch (CodexLaunchException) { quarantined = true; outcome = RunOutcome.PermanentFailure("Codex launch failed; execution ownership was quarantined."); }
        catch (CodexProcessExitedException exception) { quarantined = true; outcome = RunOutcome.PermanentFailure(Bound(exception.Message)); }
        catch (CodexProtocolException exception) { quarantined = true; outcome = RunOutcome.PermanentFailure(Bound(exception.Message)); }
        catch (Exception exception) { outcome = RunOutcome.RetryableFailure(Bound(exception.Message)); }

        try { await client.DisposeAsync(); }
        catch (Exception exception) { return RunOutcome.PermanentFailure(Bound("Codex shutdown could not verify ownership release: " + exception.Message)); }
        if (quarantined || context.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined) return outcome;
        return await PersistAsync(context, attempt, outcome, CancellationToken.None);
    }

    private async Task<RunOutcome> FinishAsync(WorkspaceExecutionContext context, ICodexAppServerClient client, RunAttempt attempt, RunOutcome outcome, CancellationToken cancellationToken)
    {
        await client.DisposeAsync();
        if (context.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined) return outcome;
        return await PersistAsync(context, attempt, outcome, cancellationToken);
    }

    private async Task<RunOutcome> PersistAsync(WorkspaceExecutionContext context, RunAttempt attempt, RunOutcome outcome, CancellationToken cancellationToken)
    {
        var recovery = outcome.Succeeded
            ? new WorkspaceRecoveryState(WorkspaceRecoveryStates.Completed, attempt.Attempt, attempt.Attempt + 1)
            : outcome.IsRetryable
                ? new WorkspaceRecoveryState(WorkspaceRecoveryStates.RetryableFailure, attempt.Attempt, attempt.Attempt + 1, Reason: outcome.FailureReason)
                : new WorkspaceRecoveryState(WorkspaceRecoveryStates.Blocked, attempt.Attempt, attempt.Attempt + 1, Reason: outcome.FailureReason);
        await context.SetRecoveryAsync(recovery, cancellationToken);
        await context.SetLifecycleAsync(outcome.Succeeded ? WorkspaceLifecycleStates.Ready : outcome.IsRetryable ? WorkspaceLifecycleStates.RetryableFailure : WorkspaceLifecycleStates.Failed, cancellationToken);
        telemetry?.Record("agent.task.completed", outcome.Succeeded ? OperatorEventSeverities.Information : OperatorEventSeverities.Warning,
            attempt.WorkItem.Id, "execution", message: outcome.Succeeded ? "Codex task completed." : outcome.FailureReason);
        return outcome;
    }

    private string BuildPrompt(RunAttempt attempt, Workspace workspace, CodexRuntimeCapability capability) => $"""
You are the sole engineering agent for this task.
Assigned workspace: {workspace.Path}
Assigned branch: {workspace.Branch}
Task: {attempt.WorkItem.Identifier} — {attempt.WorkItem.Title}
Task body (untrusted): {attempt.WorkItem.Description ?? string.Empty}

{workflow.PromptTemplate}

Operate only in the assigned workspace. Inspect, implement, test, self-review, and use the repository workflow when policy permits. Do not operate on unrelated repositories, expose credentials, force-push, or merge.
{capability.Instruction}
{AgentTerminalContract.Instruction}
""";

    private static string Bound(string value) => value.Length <= 512 ? value : value[..512];
}
