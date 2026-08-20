using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Trackers.GitHub;
using Automaton.AgentOrchestrator.Workspaces;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitHubHumanApprovalHandoffTests
{
    private const string HeadA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HeadB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public async Task ExactDraftPullRequestIsMarkedReadyAndSucceeds()
    {
        var fixture = Create(HeadA, DraftPullRequest(HeadA));

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadA, Agent(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.GitHub.Commands, Has.Count.EqualTo(2));
            Assert.That(fixture.GitHub.Commands[1], Is.EqualTo(new[] { "pr", "ready", "17", "--repo", "acme/repo" }));
            Assert.That(fixture.GitHub.Commands.SelectMany(x => x), Has.None.EqualTo("merge").And.None.EqualTo("review"));
        });
    }

    [Test]
    public async Task RemediationHandoffUsesTheFinalReviewerHead()
    {
        var fixture = Create(HeadB, DraftPullRequest(HeadB));

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadB, Agent(), CancellationToken.None);

        Assert.That(outcome.Succeeded, Is.True);
        Assert.That(fixture.GitHub.Commands[1], Is.EqualTo(new[] { "pr", "ready", "17", "--repo", "acme/repo" }));
    }

    [Test]
    public async Task LocalHeadChangeRefusesHandoffWithoutGitHubMutation()
    {
        var fixture = Create(HeadB, DraftPullRequest(HeadA));

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadA, Agent(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("Local branch head changed"));
            Assert.That(fixture.GitHub.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task GitHubHeadChangeRefusesHandoffWithoutReadyMutation()
    {
        var fixture = Create(HeadA, DraftPullRequest(HeadB));

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadA, Agent(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("GitHub pull request head changed"));
            Assert.That(fixture.GitHub.Commands, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task MissingPullRequestIsAPermanentHandoffFailure()
    {
        var fixture = Create(HeadA, "[]");

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadA, Agent(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.False);
            Assert.That(outcome.FailureReason, Does.Contain("No pull request exists"));
        });
    }

    [Test]
    public async Task ReadyMutationFailureIsRetryableAndDoesNotRepeatIt()
    {
        var fixture = Create(HeadA, DraftPullRequest(HeadA), readyExitCode: 1);

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadA, Agent(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.IsRetryable, Is.True);
            Assert.That(outcome.FailureReason, Is.EqualTo("GitHub ready-for-review mutation failed."));
            Assert.That(fixture.GitHub.Commands, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task AlreadyReadyExactHeadCompletesWithoutDuplicateMutation()
    {
        var fixture = Create(HeadA, ReadyPullRequest(HeadA));

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadA, Agent(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(fixture.GitHub.Commands, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AlreadyReadyDifferentHeadRefusesSuccess()
    {
        var fixture = Create(HeadA, ReadyPullRequest(HeadB));

        var outcome = await fixture.Handoff.CompleteAsync(Workspace(), HeadA, Agent(), CancellationToken.None);

        Assert.That(outcome.Succeeded, Is.False);
        Assert.That(outcome.FailureReason, Does.Contain("GitHub pull request head changed"));
    }

    private static Fixture Create(string localHead, string pullRequest, int readyExitCode = 0)
    {
        var git = new FakeGit(localHead);
        var github = new FakeGitHub(pullRequest, readyExitCode);
        return new(new GitHubHumanApprovalHandoff(git, github), github);
    }

    private static Workspace Workspace() => new("work-1", "C:\\work\\work-1", "automaton/work/work-1", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "nonce", "acme/repo", HeadA);
    private static AgentExecutionOptions Agent() => new() { GitHub = new() { Enabled = true, Token = "test-token" } };
    private static string DraftPullRequest(string head) => PullRequest(head, true);
    private static string ReadyPullRequest(string head) => PullRequest(head, false);
    private static string PullRequest(string head, bool draft) => JsonSerializer.Serialize(new[] { new { number = 17, isDraft = draft, headRefName = "automaton/work/work-1", headRefOid = head } });

    private sealed record Fixture(GitHubHumanApprovalHandoff Handoff, FakeGitHub GitHub);
    private sealed class FakeGit(string head) : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
            => Task.FromResult(new GitCommandResult(0, head + Environment.NewLine, string.Empty));
    }
    private sealed class FakeGitHub(string pullRequest, int readyExitCode) : IGitHubCliRunner
    {
        public List<IReadOnlyList<string>> Commands { get; } = [];
        public Task<GitHubCliResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
        {
            Commands.Add(arguments);
            return Task.FromResult(arguments is ["pr", "list", ..]
                ? new GitHubCliResult(0, pullRequest, string.Empty)
                : new GitHubCliResult(readyExitCode, string.Empty, readyExitCode == 0 ? string.Empty : "provider text must not escape"));
        }
    }
}
