using Automaton.AgentOrchestrator.Orchestration;

namespace Automaton.AgentOrchestrator.Domain;

public static class PublicationStates
{
    public const string None = "none";
    public const string Authorized = "authorized";
    public const string Committed = "committed";
    public const string Pushed = "pushed";
    public const string PullRequestCreated = "pull-request-created";
}

public sealed record PublicationRecoveryState(
    string State,
    string? CommitSha = null,
    string? RemoteBranch = null,
    int? PullRequestNumber = null,
    string? PullRequestUrl = null,
    int Revision = 1,
    string? AuthorizedParentSha = null);

public sealed record PublicationRequest(
    Workspace Workspace,
    WorkItem WorkItem,
    ManagerHandoff Manager,
    DeveloperHandoff Developer,
    ReviewerHandoff Reviewer);

public sealed record PublicationResult(
    string Repository,
    string Branch,
    string CommitSha,
    int PullRequestNumber,
    string PullRequestUrl,
    bool Draft,
    bool IsReviewedNoOp = false);

public sealed record PublicationRecoveryResult(bool Recognized, PublicationResult? Publication = null);

public sealed record PublicationRevisionRequest(
    Workspace Workspace,
    WorkItem WorkItem,
    PublicationResult CurrentPublication,
    DeveloperHandoff Developer,
    ReviewerHandoff Reviewer,
    int Revision);

public sealed record PullRequestPublication(
    int Number,
    string Url,
    string Repository,
    string HeadBranch,
    string HeadSha,
    string BaseBranch,
    string State,
    bool Draft);
