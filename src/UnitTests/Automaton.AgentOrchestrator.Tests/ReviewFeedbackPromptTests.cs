using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.ReviewFeedback;
using Automaton.AgentOrchestrator.Orchestration;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class ReviewFeedbackPromptTests
{
    [Test]
    public void PromptsTreatHumanFeedbackAsBoundedUntrustedInputAndProhibitProviderMutation()
    {
        var request = new ReviewFeedbackRemediationRequest(new("acme/widgets", 42, "branch", new string('a', 40)), new("key", "C:/assigned", "branch", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce", "acme/widgets"), new("id", "#1", "title", null, "open", []), new(new string('a', 40), ReviewFeedbackStatuses.Actionable, [new("1", ReviewFeedbackKinds.ReviewComment, "alice", new string('x', 20_000))]), 1);
        var developer = CodexReviewFeedbackRemediator.DeveloperPrompt(request);
        var reviewer = CodexReviewFeedbackRemediator.ReviewerPrompt(request, new(RoleResultStatuses.Completed, "fixed", "tests", "none"), null);
        Assert.Multiple(() => { Assert.That(developer, Does.Contain("untrusted advisory input")); Assert.That(developer, Does.Contain("Do not commit, push")); Assert.That(developer, Does.Contain("AUTOMATON_DEVELOPER_RESULT:")); Assert.That(developer, Does.Contain("status").And.Contain("summary").And.Contain("validation").And.Contain("remainingConcerns")); Assert.That(developer, Does.Contain("CONTINUE|COMPLETED|BLOCKED")); Assert.That(developer, Does.Contain("exactly one terminal line")); Assert.That(reviewer, Does.Contain("Read-only boundary")); Assert.That(developer.Length, Is.LessThan(16_000)); });
    }

    [Test]
    public void FeedbackDeveloperContinuationKeepsExactTerminalContract()
    {
        var prompt = RolePromptBuilder.Continue(AgentRoles.Developer, "continue validation");
        Assert.That(prompt, Does.Contain(RolePromptBuilder.DeveloperTerminalContract));
    }

    [Test]
    public void FeedbackReviewerPromptAndContinuationUseExactTerminalContract()
    {
        var request = new ReviewFeedbackRemediationRequest(new("acme/widgets", 42, "branch", new string('a', 40)), new("key", "C:/assigned", "branch", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce", "acme/widgets"), new("id", "#1", "title", null, "open", []), new(new string('a', 40), ReviewFeedbackStatuses.Actionable, []), 1);
        var prompt = CodexReviewFeedbackRemediator.ReviewerPrompt(request, new(RoleResultStatuses.Completed, "fixed", "tests", "none"), null);
        Assert.Multiple(() => { Assert.That(prompt, Does.Contain(RolePromptBuilder.ReviewerTerminalContract)); Assert.That(RolePromptBuilder.Continue(AgentRoles.Reviewer, "more"), Does.Contain(RolePromptBuilder.ReviewerTerminalContract)); });
    }

    [TestCase("malformed-reviewer-contract")]
    [TestCase("reviewer-turn-limit")]
    [TestCase("reviewer-blocked")]
    [TestCase("read-only-boundary-violated")]
    public void ReviewerFailuresRemainBounded(string subcategory)
    {
        var failure = CodexReviewFeedbackRemediator.ReviewerFailure(subcategory, false);
        Assert.Multiple(() => { Assert.That(failure.Category, Is.EqualTo(ReviewFeedbackFailureCategories.Reviewer)); Assert.That(failure.Subcategory, Is.EqualTo(subcategory)); Assert.That(failure.SafeReason, Is.EqualTo("human feedback Reviewer remediation failed")); });
    }

    [TestCase(ReviewFeedbackFailureCategories.Developer)]
    [TestCase(ReviewFeedbackFailureCategories.Reviewer)]
    [TestCase(ReviewFeedbackFailureCategories.Publication)]
    public void CancelledRemediationStagePropagatesCancellation(string category)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(async () => await CodexReviewFeedbackRemediator.InStage(
            category,
            "safe reason",
            cancellation.Token,
            () => Task.FromCanceled<PublicationResult>(cancellation.Token)), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void NonCancellationRemediationStageRetainsBoundedCategory()
    {
        var exception = Assert.ThrowsAsync<ReviewFeedbackRemediationException>(() => CodexReviewFeedbackRemediator.InStage(
            ReviewFeedbackFailureCategories.Developer,
            "human feedback Developer remediation failed",
            CancellationToken.None,
            () => Task.FromException<PublicationResult>(new InvalidOperationException("token=should-not-leak"))));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Category, Is.EqualTo(ReviewFeedbackFailureCategories.Developer));
            Assert.That(exception.SafeReason, Is.EqualTo("human feedback Developer remediation failed"));
            Assert.That(exception.SafeReason, Does.Not.Contain("token"));
        });
    }

    [TestCase("launch")]
    [TestCase("timeout")]
    [TestCase("process")]
    [TestCase("protocol")]
    public void DeveloperRuntimeExceptionsUseBoundedClassifications(string kind)
    {
        Exception exception = kind switch
        {
            "launch" => new CodexLaunchException("process-start", "safe"),
            "timeout" => new CodexIdleTimeoutException(),
            "process" => new CodexProcessExitedException(),
            _ => new CodexProtocolException("raw protocol payload must not escape")
        };

        var subcategory = CodexReviewFeedbackRemediator.ClassifyDeveloperException(exception);

        Assert.That(subcategory, Is.EqualTo(kind switch
        {
            "launch" => "codex-launch-failed",
            "timeout" => "codex-turn-timeout",
            "process" => "codex-process-failed",
            _ => "codex-protocol-failed"
        }));
    }

    [Test]
    public void DeveloperClassificationNeverUsesExceptionText()
    {
        var exception = new CodexProtocolException("TOKEN=secret prompt payload");
        Assert.That(CodexReviewFeedbackRemediator.ClassifyDeveloperException(exception), Is.EqualTo("codex-protocol-failed"));
    }

    [TestCase("malformed-developer-contract")]
    [TestCase("developer-turn-limit")]
    [TestCase("blocked")]
    public void DeveloperContractFailuresRemainBounded(string subcategory)
    {
        var failure = CodexReviewFeedbackRemediator.DeveloperFailure(subcategory, false);
        Assert.Multiple(() =>
        {
            Assert.That(failure.Category, Is.EqualTo(ReviewFeedbackFailureCategories.Developer));
            Assert.That(failure.Subcategory, Is.EqualTo(subcategory));
            Assert.That(failure.SafeReason, Is.EqualTo("human feedback Developer remediation failed"));
        });
    }
}
