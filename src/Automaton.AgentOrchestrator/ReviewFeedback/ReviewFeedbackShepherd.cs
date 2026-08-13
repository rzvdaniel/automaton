using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Ci;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Publication;
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
            catch (ReviewFeedbackProviderException exception)
            {
                return ReviewFeedbackResult.Blocked("human feedback provider failed", current.HeadSha, round, ReviewFeedbackFailureCategories.Provider, exception.IsRetryable);
            }
            if (!string.Equals(snapshot.HeadSha, current.HeadSha, StringComparison.OrdinalIgnoreCase))
                return ReviewFeedbackResult.Blocked("Review feedback provider returned an unexpected head SHA.", current.HeadSha, round);
            await workspaces.UpdateReviewFeedbackStateAsync(workspace, new(snapshot.Status, current.HeadSha, round, clock.UtcNow), CancellationToken.None);
            var durable = await workspaces.GetReviewFeedbackStateAsync(workspace, cancellationToken);
            var actionable = snapshot.Items.Where(item => durable?.Consumed is null || !durable.Consumed.Any(consumed =>
                consumed.RepositoryIdentity.Equals(current.RepositoryIdentity, StringComparison.OrdinalIgnoreCase)
                && consumed.PullRequestNumber == current.PullRequestNumber
                && consumed.FeedbackId == item.Id
                && consumed.Version == (item.CommitSha ?? current.HeadSha))).ToArray();
            if (snapshot.Status == ReviewFeedbackStatuses.Actionable && actionable.Length == 0)
            {
                telemetry?.Record("human-feedback.replay-suppressed", OperatorEventSeverities.Information, workItem.Id, "human-feedback", metadata: new Dictionary<string, string> { ["count"] = snapshot.Items.Count.ToString() });
                await delay.DelayAsync(TimeSpan.FromMilliseconds(options.ReviewFeedback.PollIntervalMilliseconds), cancellationToken);
                continue;
            }
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

            PublicationResult publication;
            try { publication = await remediator.RemediateAsync(new(current, workspace, workItem, snapshot with { Items = actionable }, round + 1), cancellationToken); }
            catch (ReviewFeedbackRemediationException exception)
            {
                return ReviewFeedbackResult.Blocked(exception.SafeReason, current.HeadSha, round, exception.Category, exception.IsRetryable) with { FailureSubcategory = exception.Subcategory };
            }
            catch (PublicationException exception)
            {
                return ReviewFeedbackResult.Blocked("human feedback publication failed", current.HeadSha, round, ReviewFeedbackFailureCategories.Publication, exception.IsRetryable);
            }
            telemetry?.Record("feedback.remediation.published", OperatorEventSeverities.Information, workItem.Id, "feedback-remediation", AgentRoles.Developer,
                "Human feedback remediation was independently approved and published.", new Dictionary<string, string> { ["headSha"] = publication.CommitSha });
            if (!MatchesRevision(publication, current))
                return ReviewFeedbackResult.Blocked("Feedback remediation returned an invalid publication identity.", current.HeadSha, round);
            round++;
            var next = current with { HeadSha = publication.CommitSha, RemediationRound = round, PublicationRevision = current.PublicationRevision + 1 };
            CiShepherdResult ciResult;
            try
            {
                ciResult = await ci.ShepherdAsync(new(next.RepositoryIdentity, next.PullRequestNumber, next.HeadBranch, next.HeadSha), workspace, workItem, cancellationToken);
            }
            catch (CiProviderException exception)
            {
                return ReviewFeedbackResult.Blocked("human feedback CI failed", next.HeadSha, round, ReviewFeedbackFailureCategories.Ci, exception.IsRetryable);
            }
            if (!ciResult.Succeeded)
                return ReviewFeedbackResult.Blocked("human feedback CI failed", ciResult.HeadSha ?? next.HeadSha, round, ReviewFeedbackFailureCategories.Ci, ciResult.IsRetryable);
            current = next with { HeadSha = ciResult.HeadSha ?? next.HeadSha, PublicationRevision = next.PublicationRevision + ciResult.RemediationRound };
            var consumed = actionable.Select(item => new ConsumedReviewFeedback(current.RepositoryIdentity, current.PullRequestNumber, item.Id, item.CommitSha ?? next.HeadSha, current.HeadSha, current.PublicationRevision, clock.UtcNow)).ToArray();
            var state = await workspaces.GetReviewFeedbackStateAsync(workspace, CancellationToken.None);
            await workspaces.UpdateReviewFeedbackStateAsync(workspace, new(ReviewFeedbackStatuses.AwaitingHuman, current.HeadSha, round, clock.UtcNow, [..(state?.Consumed ?? []), ..consumed]), CancellationToken.None);
        }
    }

    private static bool MatchesRevision(PublicationResult publication, ReviewFeedbackTarget current)
        => string.Equals(publication.Repository, current.RepositoryIdentity, StringComparison.OrdinalIgnoreCase)
           && publication.PullRequestNumber == current.PullRequestNumber
           && string.Equals(publication.Branch, current.HeadBranch, StringComparison.Ordinal)
           && publication.CommitSha.Length == 40 && publication.CommitSha.All(Uri.IsHexDigit)
           && !string.Equals(publication.CommitSha, current.HeadSha, StringComparison.OrdinalIgnoreCase);
}

public sealed class ReviewFeedbackRemediationException(string category, string safeReason, bool isRetryable = false, string? subcategory = null) : Exception(safeReason)
{
    public string Category { get; } = category;
    public string SafeReason { get; } = safeReason;
    public bool IsRetryable { get; } = isRetryable;
    public string Subcategory { get; } = subcategory ?? "unexpected";
}
