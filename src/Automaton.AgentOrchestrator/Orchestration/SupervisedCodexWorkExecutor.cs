using System.Text.Json;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Trackers.GitHub;
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

public sealed record ReviewTerminalResult(string Verdict, string Summary);
public enum ReviewTerminalParseFailure
{
    None,
    EmptyResponse,
    MissingTerminalLine,
    InvalidJson,
    MissingVerdict,
    UnsupportedVerdict,
    MissingSummary,
    SummaryTooLong,
    ResponseTooLong,
    MultipleTerminalLines,
    TurnNotCompleted
}
public static class ReviewTerminalContract
{
    public const string Instruction = "End with exactly one terminal line: REVIEW_RESULT: {\"verdict\":\"APPROVED|CHANGES_REQUIRED|BLOCKED\",\"summary\":\"bounded text\"}";
    public static bool TryParse(string? response, out ReviewTerminalResult result)
        => TryParse(response, out result, out _);

    public static bool TryParse(string? response, out ReviewTerminalResult result, out ReviewTerminalParseFailure failure)
    {
        result = new(string.Empty, string.Empty);
        failure = ReviewTerminalParseFailure.None;
        if (string.IsNullOrWhiteSpace(response)) { failure = ReviewTerminalParseFailure.EmptyResponse; return false; }
        if (response.Length > 16_384) { failure = ReviewTerminalParseFailure.ResponseTooLong; return false; }
        const string prefix = "REVIEW_RESULT: ";
        var terminalLineCount = response.Split('\n').Count(line => line.TrimEnd('\r').StartsWith(prefix, StringComparison.Ordinal));
        if (terminalLineCount == 0) { failure = ReviewTerminalParseFailure.MissingTerminalLine; return false; }
        if (terminalLineCount > 1) { failure = ReviewTerminalParseFailure.MultipleTerminalLines; return false; }
        var line = response.TrimEnd().Split('\n').Last().TrimEnd('\r');
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) { failure = ReviewTerminalParseFailure.MissingTerminalLine; return false; }
        try
        {
            using var document = JsonDocument.Parse(line[prefix.Length..]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { failure = ReviewTerminalParseFailure.InvalidJson; return false; }
            if (!root.TryGetProperty("verdict", out var verdict)) { failure = ReviewTerminalParseFailure.MissingVerdict; return false; }
            if (verdict.ValueKind != JsonValueKind.String) { failure = ReviewTerminalParseFailure.UnsupportedVerdict; return false; }
            var value = verdict.GetString() ?? string.Empty;
            if (value is not ("APPROVED" or "CHANGES_REQUIRED" or "BLOCKED")) { failure = ReviewTerminalParseFailure.UnsupportedVerdict; return false; }
            if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.String) { failure = ReviewTerminalParseFailure.MissingSummary; return false; }
            var text = summary.GetString() ?? string.Empty;
            if (text.Length > 512) { failure = ReviewTerminalParseFailure.SummaryTooLong; return false; }
            result = new(value, text); return true;
        }
        catch (JsonException) { failure = ReviewTerminalParseFailure.InvalidJson; return false; }
    }
}

public sealed class SupervisedCodexWorkExecutor(
    IWorkspaceManager workspaceManager,
    ICodexAppServerClientFactory clients,
    WorkflowDefinition workflow,
    IHumanApprovalHandoff humanApprovalHandoff,
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

        var profile = workflow.Options.Agent.Developer;
        var capability = CodexRuntimeCapability.From(workflow.Options.Agent, profile);
        var client = clients.Create(context, profile, capability.ChildEnvironment);
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
                if (terminal.Status == "COMPLETED")
                {
                    await client.DisposeAsync();
                    if (workflow.Options.Agent.Review.Enabled)
                        return await ReviewAsync(context, attempt, RunOutcome.Success(), CancellationToken.None);
                    return await PersistAsync(context, attempt, RunOutcome.Success(), CancellationToken.None);
                }
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
        catch (CodexIdleTimeoutException exception) { RecordTimeout(telemetry, attempt, exception.Diagnostic); outcome = RunOutcome.RetryableFailure(Bound(exception.Message)); }
        catch (CodexTurnTimeoutException exception) { RecordTimeout(telemetry, attempt, exception.Diagnostic); outcome = RunOutcome.RetryableFailure(Bound(exception.Message)); }
        catch (CodexLaunchException) { quarantined = true; outcome = RunOutcome.PermanentFailure("Codex launch failed; execution ownership was quarantined."); }
        catch (CodexProcessExitedException exception) { quarantined = true; outcome = RunOutcome.PermanentFailure(Bound(exception.Message)); }
        catch (CodexProtocolException exception) { quarantined = true; outcome = RunOutcome.PermanentFailure(Bound(exception.Message)); }
        catch (Exception exception) { outcome = RunOutcome.RetryableFailure(Bound(exception.Message)); }

        try { await client.DisposeAsync(); }
        catch (Exception exception) { return RunOutcome.PermanentFailure(Bound("Codex shutdown could not verify ownership release: " + exception.Message)); }
        if (quarantined || context.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined) return outcome;
        return await PersistAsync(context, attempt, outcome, CancellationToken.None);
    }

    private async Task<RunOutcome> ReviewAsync(WorkspaceExecutionContext context, RunAttempt attempt, RunOutcome developerOutcome, CancellationToken cancellationToken)
    {
        for (var round = 1; round <= workflow.Options.Agent.Review.MaxRounds; round++)
        {
            var reviewer = clients.Create(context, workflow.Options.Agent.Reviewer, CodexRuntimeCapability.From(workflow.Options.Agent, workflow.Options.Agent.Reviewer).ChildEnvironment);
            try
            {
                await reviewer.InitializeAsync(cancellationToken);
                var thread = await reviewer.StartThreadAsync(cancellationToken);
                var prompt = $"""You are an independent read-only reviewer. Inspect the current assigned workspace and diff for task {attempt.WorkItem.Identifier} — {attempt.WorkItem.Title}. Review correctness, requirements, tests, regressions, and unrelated changes. Do not modify files, commit, push, or change GitHub state.\n\n{workflow.PromptTemplate}\n\n{CodexRuntimeCapability.From(workflow.Options.Agent, workflow.Options.Agent.Reviewer).Instruction}\n{ReviewTerminalContract.Instruction}""";
                var reviewedHead = await humanApprovalHandoff.CaptureReviewedHeadAsync(context.Workspace, cancellationToken);
                var turn = await reviewer.StartTurnAsync(thread.Id, prompt, cancellationToken);
                var result = await reviewer.WaitForTurnCompletionAsync(turn, cancellationToken);
                var parseFailure = ReviewTerminalParseFailure.None;
                if (!string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    parseFailure = ReviewTerminalParseFailure.TurnNotCompleted;
                    return await FinishReviewAsync(context, reviewer, attempt, RunOutcome.PermanentFailure(FormatNonCompletedReviewerTurn(result)), cancellationToken);
                }
                if (!ReviewTerminalContract.TryParse(result.FinalResponse, out var review, out parseFailure))
                    return await FinishReviewAsync(context, reviewer, attempt, RunOutcome.PermanentFailure($"Reviewer returned an invalid review result ({parseFailure.ToString().ToLowerInvariant()})."), cancellationToken);
                if (review.Verdict == "APPROVED")
                {
                    await reviewer.DisposeAsync();
                    var handoff = await humanApprovalHandoff.CompleteAsync(context.Workspace, reviewedHead, workflow.Options.Agent, cancellationToken);
                    if (context.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined) return handoff;
                    return await PersistAsync(context, attempt, handoff, cancellationToken);
                }
                await reviewer.DisposeAsync();
                if (review.Verdict == "BLOCKED")
                    return await PersistAsync(context, attempt, RunOutcome.PermanentFailure(Bound(review.Summary)), cancellationToken);
                if (round == workflow.Options.Agent.Review.MaxRounds)
                    return await PersistAsync(context, attempt, RunOutcome.PermanentFailure(Bound(review.Summary)), cancellationToken);
                var remediation = await RemediateAsync(context, attempt, review.Summary, cancellationToken);
                if (!remediation.Succeeded) return remediation;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { try { await reviewer.DisposeAsync(); } catch { } throw; }
            catch (CodexIdleTimeoutException exception) { return await FinishReviewAsync(context, reviewer, attempt, RunOutcome.PermanentFailure(ReviewFailureReason("stall_timeout", exception.Diagnostic)), CancellationToken.None); }
            catch (CodexTurnTimeoutException exception) { return await FinishReviewAsync(context, reviewer, attempt, RunOutcome.PermanentFailure(ReviewFailureReason("turn_timeout", exception.Diagnostic)), CancellationToken.None); }
            catch (CodexProcessExitedException) { return await FinishReviewAsync(context, reviewer, attempt, RunOutcome.PermanentFailure("Reviewer turn failed (termination=process_exited)."), CancellationToken.None); }
            catch (CodexProtocolException) { return await FinishReviewAsync(context, reviewer, attempt, RunOutcome.PermanentFailure("Reviewer turn failed (termination=protocol_failure)."), CancellationToken.None); }
            catch (Exception) { return await FinishReviewAsync(context, reviewer, attempt, RunOutcome.PermanentFailure("Reviewer turn failed (termination=unknown)."), CancellationToken.None); }
        }
        return developerOutcome;
    }

    private async Task<RunOutcome> RemediateAsync(WorkspaceExecutionContext context, RunAttempt attempt, string findings, CancellationToken cancellationToken)
    {
        var profile = workflow.Options.Agent.Developer;
        var capability = CodexRuntimeCapability.From(workflow.Options.Agent, profile);
        var client = clients.Create(context, profile, capability.ChildEnvironment);
        try
        {
            await client.InitializeAsync(cancellationToken);
            var thread = await client.StartThreadAsync(cancellationToken);
            var prompt = $"Continue the assigned task in the same workspace. Address these independent reviewer findings, run focused tests, and update the existing branch/PR without merging: {Bound(findings)}\n{profile.Instructions}\n{AgentTerminalContract.Instruction}";
            var turn = await client.StartTurnAsync(thread.Id, prompt, cancellationToken);
            var result = await client.WaitForTurnCompletionAsync(turn, cancellationToken);
            if (!string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase) || !AgentTerminalContract.TryParse(result.FinalResponse, out var terminal))
                return await FinishAsync(context, client, attempt, RunOutcome.PermanentFailure("Developer remediation returned a malformed task result."), cancellationToken);
            var outcome = terminal.Status == "COMPLETED" ? RunOutcome.Success() : terminal.Status == "RETRYABLE" ? RunOutcome.RetryableFailure(Bound(terminal.Summary)) : RunOutcome.PermanentFailure(Bound(terminal.Summary));
            await client.DisposeAsync();
            if (outcome.Succeeded && context.Workspace.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.Quarantined)
                return outcome;
            if (context.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined) return outcome;
            return await PersistAsync(context, attempt, outcome, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return await FinishAsync(context, client, attempt, RunOutcome.PermanentFailure(Bound(exception.Message)), CancellationToken.None); }
    }

    private async Task<RunOutcome> FinishReviewAsync(WorkspaceExecutionContext context, ICodexAppServerClient reviewer, RunAttempt attempt, RunOutcome outcome, CancellationToken cancellationToken)
    {
        await reviewer.DisposeAsync();
        if (context.Workspace.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Quarantined) return outcome;
        return await PersistAsync(context, attempt, outcome, cancellationToken);
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

Execution profile: {workflow.Options.Agent.Developer.Name}; model: {workflow.Options.Agent.Developer.Model}; workspace access: {workflow.Options.Agent.Developer.WorkspaceAccess}; GitHub access: {workflow.Options.Agent.Developer.GitHubAccess}.
{workflow.Options.Agent.Developer.Instructions}

Operate only in the assigned workspace. Inspect, implement, test, self-review, and use the repository workflow when policy permits. Do not operate on unrelated repositories, expose credentials, force-push, or merge.
{capability.Instruction}
{AgentTerminalContract.Instruction}
""";

    private static string Bound(string value) => value.Length <= 512 ? value : value[..512];
    private static string BoundEnum(string? value) => value?.ToLowerInvariant() switch
    {
        "failed" or "interrupted" or "completed" or "provider_failed" or "invalid_request" or "authentication_failed" or "permission_denied" or "rate_limited" or "unknown_provider_failure" or "turn/completed" or "turn/started" or "item/started" or "item/completed" or "unknown" or "command_approval" or "file_approval" or "user_input" or "permissions" => value!.ToLowerInvariant(),
        _ => "unknown"
    };
    internal static string FormatNonCompletedReviewerTurn(CodexTurnResult result)
    {
        var diagnostic = result.Diagnostic;
        return diagnostic is null
            ? "Reviewer returned a non-completed turn (turn-not-completed; status=unknown, final_response_present=unknown, last_event=unknown, process_alive=unknown, termination=unknown, provider_failure=unknown_provider_failure, server_request_count=0, server_request_declined_count=0, last_server_request_type=unknown, total_elapsed_ms=unknown, inactivity_ms=unknown, turn_completed_observed=unknown)."
            : $"Reviewer returned a non-completed turn (status={BoundEnum(diagnostic.Status)}, final_response_present={diagnostic.FinalResponsePresent.ToString().ToLowerInvariant()}, last_event={BoundEnum(diagnostic.LastEventType)}, process_alive={diagnostic.ProcessAlive?.ToString().ToLowerInvariant() ?? "unknown"}, termination={BoundEnum(diagnostic.Termination)}, provider_failure={BoundEnum(diagnostic.ProviderFailureCategory)}, server_request_count={diagnostic.ServerRequestCount}, server_request_declined_count={diagnostic.ServerRequestDeclinedCount}, last_server_request_type={BoundEnum(diagnostic.LastServerRequestType)}, total_elapsed_ms={(long)diagnostic.TotalElapsed.TotalMilliseconds}, inactivity_ms={(long)diagnostic.InactivityDuration.TotalMilliseconds}, turn_completed_observed={diagnostic.TurnCompletedObserved.ToString().ToLowerInvariant()}).";
    }
    private static string ReviewFailureReason(string termination, CodexTimeoutDiagnostic? diagnostic)
        => diagnostic is null
            ? $"Reviewer turn failed (termination={termination}, status=unknown, final_response_present=unknown, last_event=unknown, process_alive=unknown, total_elapsed_ms=unknown, inactivity_ms=unknown, turn_completed_observed=unknown)."
            : $"Reviewer turn failed (termination={termination}, status=unknown, final_response_present=unknown, last_event={BoundEnum(diagnostic.LastEventType)}, process_alive={diagnostic.ProcessAlive?.ToString().ToLowerInvariant() ?? "unknown"}, total_elapsed_ms={(long)diagnostic.TotalElapsed.TotalMilliseconds}, inactivity_ms={(long)diagnostic.InactivityDuration.TotalMilliseconds}, turn_completed_observed=false).";

    private static void RecordTimeout(IOperatorTelemetry? telemetry, RunAttempt attempt, CodexTimeoutDiagnostic? diagnostic)
    {
        if (telemetry is null || diagnostic is null) return;
        telemetry.Record("agent.codex.timeout", OperatorEventSeverities.Warning, attempt.WorkItem.Id, "execution", metadata: new Dictionary<string, string>
        {
            ["timeoutKind"] = diagnostic.Kind,
            ["configuredTimeoutMs"] = ((long)diagnostic.ConfiguredTimeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["totalElapsedMs"] = ((long)diagnostic.TotalElapsed.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["inactivityMs"] = ((long)diagnostic.InactivityDuration.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["lastActivityUtc"] = diagnostic.LastActivityUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["lastEventType"] = Bound(diagnostic.LastEventType ?? "unknown"),
            ["processAlive"] = diagnostic.ProcessAlive?.ToString() ?? "unknown"
        });
    }
}
