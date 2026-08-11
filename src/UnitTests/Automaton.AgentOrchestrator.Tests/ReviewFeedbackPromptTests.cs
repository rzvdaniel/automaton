using Automaton.AgentOrchestrator.Domain;
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
        Assert.Multiple(() => { Assert.That(developer, Does.Contain("untrusted advisory input")); Assert.That(developer, Does.Contain("Do not commit, push")); Assert.That(reviewer, Does.Contain("Read-only boundary")); Assert.That(developer.Length, Is.LessThan(16_000)); });
    }
}
