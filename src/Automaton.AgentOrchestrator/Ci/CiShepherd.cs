using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Ci;

public sealed class SystemCiPollDelay : ICiPollDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class CiShepherd(
    ICiProvider provider,
    ICiRemediator remediator,
    IWorkspaceManager workspaces,
    IClock clock,
    ICiPollDelay delay,
    OrchestratorOptions options,
    IOperatorTelemetry? telemetry = null) : ICiShepherd
{
    public async Task<CiShepherdResult> ShepherdAsync(CiTarget target, Workspace workspace, WorkItem workItem, CancellationToken cancellationToken)
    {
        if (!options.Ci.Enabled) return CiShepherdResult.Success(target.HeadSha, 0);
        var current = target;
        var currentWorkspace = workspace;
        for (var round = 0; round <= options.Ci.MaxRemediationRounds; round++)
        {
            var observed = await ObserveAsync(current, currentWorkspace, round, cancellationToken);
            var observation = observed.Snapshot;
            currentWorkspace = observed.Workspace;
            telemetry?.Record("ci.snapshot", observation.Status == CiStatuses.Failed ? OperatorEventSeverities.Warning : OperatorEventSeverities.Information,
                workItem.Id, "ci", message: observation.Status, metadata: new Dictionary<string, string> { ["headSha"] = current.HeadSha, ["round"] = round.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            if (observation.Status == CiStatuses.Passed) return CiShepherdResult.Success(current.HeadSha, round, currentWorkspace);
            if (observation.Status != CiStatuses.Failed)
                return CiShepherdResult.Blocked(observation.Checks.FirstOrDefault()?.FailureSummary ?? "CI state is indeterminate.", current.HeadSha, round, currentWorkspace);
            if (round == options.Ci.MaxRemediationRounds)
                return CiShepherdResult.Blocked("CI still fails after the configured remediation rounds.", current.HeadSha, round, currentWorkspace);

            var remediation = await remediator.RemediateAsync(new(current, currentWorkspace, workItem, observation, round + 1), cancellationToken);
            var publication = remediation.Publication;
            currentWorkspace = remediation.Workspace;
            telemetry?.Record("ci.remediation.published", OperatorEventSeverities.Information, workItem.Id, "ci-remediation", AgentRoles.Developer,
                "CI remediation revision was independently approved and published.", new Dictionary<string, string> { ["headSha"] = publication.CommitSha });
            if (!string.Equals(publication.Repository, current.RepositoryIdentity, StringComparison.OrdinalIgnoreCase)
                || publication.PullRequestNumber != current.PullRequestNumber
                || !string.Equals(publication.Branch, current.HeadBranch, StringComparison.Ordinal)
                || !IsSha(publication.CommitSha) || string.Equals(publication.CommitSha, current.HeadSha, StringComparison.OrdinalIgnoreCase))
                return CiShepherdResult.Blocked("CI remediation returned an invalid publication identity.", current.HeadSha, round, currentWorkspace);
            current = current with { HeadSha = publication.CommitSha };
        }
        return CiShepherdResult.Blocked("CI remediation budget was exhausted.", current.HeadSha, options.Ci.MaxRemediationRounds, currentWorkspace);
    }

    private async Task<(CiSnapshot Snapshot, Workspace Workspace)> ObserveAsync(CiTarget target, Workspace workspace, int round, CancellationToken cancellationToken)
    {
        var started = clock.UtcNow;
        while (true)
        {
            CiSnapshot snapshot;
            try { snapshot = await provider.GetAsync(target, cancellationToken); }
            catch (CiProviderException exception) when (exception.IsRetryable)
            {
                if (clock.UtcNow - started >= TimeSpan.FromMilliseconds(options.Ci.TimeoutMilliseconds))
                    throw new CiProviderException("CI observation did not recover before the configured timeout.");
                await delay.DelayAsync(TimeSpan.FromMilliseconds(options.Ci.PollIntervalMilliseconds), cancellationToken);
                continue;
            }
            if (!string.Equals(snapshot.HeadSha, target.HeadSha, StringComparison.OrdinalIgnoreCase))
                throw new CiProviderException("CI provider returned a snapshot for an unexpected commit SHA.");
            workspace = await workspaces.UpdateCiStateAsync(workspace, new(snapshot.Status, target.HeadSha, round, clock.UtcNow,
                snapshot.Status == CiStatuses.Failed ? BuildFailureReason(snapshot) : null), CancellationToken.None);
            if (snapshot.Status != CiStatuses.Pending) return (snapshot, workspace);
            if (clock.UtcNow - started >= TimeSpan.FromMilliseconds(options.Ci.TimeoutMilliseconds))
                return (new(target.HeadSha, CiStatuses.Indeterminate, [new("CI timeout", CiStatuses.Indeterminate, FailureSummary: "CI did not reach a terminal result within the configured timeout.")]), workspace);
            await delay.DelayAsync(TimeSpan.FromMilliseconds(options.Ci.PollIntervalMilliseconds), cancellationToken);
        }
    }

    private static string BuildFailureReason(CiSnapshot snapshot)
        => string.Join("; ", snapshot.Checks.Where(check => check.Status == CiStatuses.Failed).Take(10).Select(check => $"{check.Name}: {check.Conclusion ?? "failed"}"));
    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);
}
