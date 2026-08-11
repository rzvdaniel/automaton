using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.ReviewFeedback;

public sealed class SystemReviewFeedbackPollDelay : IReviewFeedbackPollDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class ReviewFeedbackShepherd(
    IReviewFeedbackProvider provider,
    IReviewFeedbackRemediator remediator,
    ICiShepherd ci,
    IWorkspaceManager workspaces,
    IClock clock,
    IReviewFeedbackPollDelay delay,
    OrchestratorOptions options,
    IOperatorTelemetry? telemetry = null) : IReviewFeedbackShepherd
{
    public async Task<ReviewFeedbackResult> ShepherdAsync(ReviewFeedbackTarget target, Workspace workspace, WorkItem workItem, CancellationToken cancellationToken)
    {
        if (!options.ReviewFeedback.Enabled) return ReviewFeedbackResult.Success(target.HeadSha, 0);
        var current = target;
        var round = target.RemediationRound;
        while (true)
        {
            ReviewFeedbackSnapshot snapshot;
            try { snapshot = await provider.GetAsync(current, cancellationToken); }
            catch (ReviewFeedbackProviderException exception) when (exception.IsRetryable)
            {
                await delay.DelayAsync(TimeSpan.FromMilliseconds(options.ReviewFeedback.PollIntervalMilliseconds), cancellationToken);
                continue;
            }
            if (!string.Equals(snapshot.HeadSha, current.HeadSha, StringComparison.OrdinalIgnoreCase))
                return ReviewFeedbackResult.Blocked("Review feedback provider returned an unexpected head SHA.", current.HeadSha, round);
            await workspaces.UpdateReviewFeedbackStateAsync(workspace, new(snapshot.Status, current.HeadSha, round, clock.UtcNow), CancellationToken.None);
            telemetry?.Record("feedback.snapshot", snapshot.Status == ReviewFeedbackStatuses.Actionable ? OperatorEventSeverities.Warning : OperatorEventSeverities.Information,
                workItem.Id, "human-feedback", message: snapshot.Status, metadata: new Dictionary<string, string> { ["headSha"] = current.HeadSha, ["round"] = round.ToString(System.Globalization.CultureInfo.InvariantCulture), ["itemCount"] = snapshot.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            if (snapshot.Status == ReviewFeedbackStatuses.Approved) return ReviewFeedbackResult.Success(current.HeadSha, round);
            if (snapshot.Status == ReviewFeedbackStatuses.AwaitingHuman)
            {
                await delay.DelayAsync(TimeSpan.FromMilliseconds(options.ReviewFeedback.PollIntervalMilliseconds), cancellationToken);
                continue;
            }
            if (snapshot.Status != ReviewFeedbackStatuses.Actionable)
                return ReviewFeedbackResult.Blocked("Review feedback state is indeterminate.", current.HeadSha, round);
            if (round >= options.ReviewFeedback.MaxRemediationRounds)
                return ReviewFeedbackResult.Blocked("Human review feedback remediation rounds were exhausted.", current.HeadSha, round);

            var publication = await remediator.RemediateAsync(new(current, workspace, workItem, snapshot, round + 1), cancellationToken);
            telemetry?.Record("feedback.remediation.published", OperatorEventSeverities.Information, workItem.Id, "feedback-remediation", AgentRoles.Developer,
                "Human feedback remediation was independently approved and published.", new Dictionary<string, string> { ["headSha"] = publication.CommitSha });
            if (!MatchesRevision(publication, current))
                return ReviewFeedbackResult.Blocked("Feedback remediation returned an invalid publication identity.", current.HeadSha, round);
            round++;
            var next = current with { HeadSha = publication.CommitSha, RemediationRound = round, PublicationRevision = current.PublicationRevision + 1 };
            var ciResult = await ci.ShepherdAsync(new(next.RepositoryIdentity, next.PullRequestNumber, next.HeadBranch, next.HeadSha), workspace, workItem, cancellationToken);
            if (!ciResult.Succeeded)
                return ReviewFeedbackResult.Blocked(ciResult.FailureReason ?? "Republished feedback remediation did not pass CI.", ciResult.HeadSha ?? next.HeadSha, round);
            current = next with { HeadSha = ciResult.HeadSha ?? next.HeadSha, PublicationRevision = next.PublicationRevision + ciResult.RemediationRound };
        }
    }

    private static bool MatchesRevision(PublicationResult publication, ReviewFeedbackTarget current)
        => string.Equals(publication.Repository, current.RepositoryIdentity, StringComparison.OrdinalIgnoreCase)
           && publication.PullRequestNumber == current.PullRequestNumber
           && string.Equals(publication.Branch, current.HeadBranch, StringComparison.Ordinal)
           && publication.CommitSha.Length == 40 && publication.CommitSha.All(Uri.IsHexDigit)
           && !string.Equals(publication.CommitSha, current.HeadSha, StringComparison.OrdinalIgnoreCase);
}
