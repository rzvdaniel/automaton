using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;

namespace Automaton.AgentOrchestrator.Workspaces;

public sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface IGitCommandRunner
{
    Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class ProcessGitCommandRunner : IGitCommandRunner
{
    public async Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new WorkspaceException("Unable to start Git for workspace management.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new GitCommandResult(process.ExitCode, await output, await error);
    }
}

public sealed class GitWorktreeWorkspaceManager : IWorkspaceManager
{
    private const int CurrentSchemaVersion = 6;
    private const string SidecarDirectoryName = ".automaton-agent-orchestrator";
    private readonly string workspaceRoot;
    private readonly IGitCommandRunner git;
    private readonly IClock clock;
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };

    public GitWorktreeWorkspaceManager(string workspaceRoot, IGitCommandRunner git, IClock clock)
    {
        this.workspaceRoot = RequireRoot(workspaceRoot);
        this.git = git;
        this.clock = clock;
    }

    public async Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = request with { Repository = request.Repository with { RootPath = Path.GetFullPath(request.Repository.RootPath) } };
        ValidateRepository(request.Repository);
        Directory.CreateDirectory(workspaceRoot);
        EnsureNoReparsePoints(workspaceRoot);

        var key = CreateKey(request.WorkItem.Id);
        var workspacePath = GetWorkspacePath(key);
        var sidecarPath = GetSidecarPath(key);
        var branch = $"automaton/work/{key}";
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken);

        if (sidecar is not null)
        {
            sidecar = await MigrateSidecarAsync(sidecarPath, sidecar, cancellationToken);
            ValidateSidecar(sidecar, request, key, workspacePath, branch);
            if (!string.Equals(sidecar.ExecutionOwnershipStatus, WorkspaceExecutionOwnershipStates.None, StringComparison.Ordinal))
                throw new WorkspaceOwnershipException("The workspace has unresolved execution ownership and cannot be reused.");

            if (Directory.Exists(workspacePath))
            {
                EnsureNoReparsePoints(workspacePath);
                await EnsureRegisteredWorktreeAsync(request.Repository.RootPath, workspacePath, sidecar.Branch, cancellationToken);
                return ToWorkspace(sidecar);
            }

            return await CreateWorktreeAsync(request, sidecar with { LifecycleStatus = WorkspaceLifecycleStates.Preparing, UpdatedAtUtc = clock.UtcNow }, sidecarPath, cancellationToken);
        }

        if (Directory.Exists(workspacePath))
            throw new WorkspaceRecoveryException("A workspace directory without daemon-owned sidecar state cannot be reused or removed.");

        var baseCommitSha = await ResolveBaseShaAsync(request.Repository.RootPath, request.Repository.BaseBranch, cancellationToken);
        var created = new WorkspaceSidecar(
            key, request.WorkItem.Id, request.Repository.Identity, request.Repository.RootPath, workspacePath, branch,
            WorkspaceLifecycleStates.Preparing, WorkspaceExecutionOwnershipStates.None, Guid.NewGuid().ToString("N"), clock.UtcNow, clock.UtcNow,
            CurrentSchemaVersion, WorkspaceRecoveryStates.Ready, BaseCommitSha: baseCommitSha, BaseBranch: request.Repository.BaseBranch);
        return await CreateWorktreeAsync(request, created, sidecarPath, cancellationToken);
    }

    public async Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string lifecycleStatus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!IsKnownLifecycleStatus(lifecycleStatus)) throw new WorkspaceException("Unknown workspace lifecycle status.");
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        var updated = sidecar with { LifecycleStatus = lifecycleStatus, UpdatedAtUtc = clock.UtcNow };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string executionOwnershipStatus, string launchNonce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!IsKnownOwnershipStatus(executionOwnershipStatus) || string.IsNullOrWhiteSpace(launchNonce))
            throw new WorkspaceException("Workspace execution ownership state is invalid.");
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        if (executionOwnershipStatus == WorkspaceExecutionOwnershipStates.Active
            && sidecar.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.None)
            throw new WorkspaceOwnershipException("Workspace execution ownership is already unresolved.");
        if (executionOwnershipStatus == WorkspaceExecutionOwnershipStates.None
            && sidecar.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.Active)
            throw new WorkspaceOwnershipException("Only verified active execution ownership can be released.");
        if (executionOwnershipStatus == WorkspaceExecutionOwnershipStates.None
            && !string.Equals(launchNonce, sidecar.LaunchNonce, StringComparison.Ordinal))
            throw new WorkspaceOwnershipException("Execution ownership can be released only with its matching launch nonce.");
        var updated = sidecar with
        {
            ExecutionOwnershipStatus = executionOwnershipStatus,
            LaunchNonce = launchNonce,
            ProcessId = executionOwnershipStatus == WorkspaceExecutionOwnershipStates.None ? null : sidecar.ProcessId,
            ProcessStartedAtUtc = executionOwnershipStatus == WorkspaceExecutionOwnershipStates.None ? null : sidecar.ProcessStartedAtUtc,
            ProcessGroupId = executionOwnershipStatus == WorkspaceExecutionOwnershipStates.None ? null : sidecar.ProcessGroupId,
            FencingKind = executionOwnershipStatus == WorkspaceExecutionOwnershipStates.None ? null : sidecar.FencingKind,
            ExecutionStartedAtUtc = executionOwnershipStatus == WorkspaceExecutionOwnershipStates.None ? null : sidecar.ExecutionStartedAtUtc,
            UpdatedAtUtc = clock.UtcNow
        };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<Workspace> RecordExecutionIdentityAsync(Workspace workspace, DurableExecutionIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(identity);
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        if (sidecar.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.Active
            || !string.Equals(sidecar.LaunchNonce, identity.LaunchNonce, StringComparison.Ordinal))
            throw new WorkspaceOwnershipException("Process identity can be recorded only for matching active ownership.");
        var updated = sidecar with
        {
            ProcessId = identity.ProcessId,
            ProcessStartedAtUtc = identity.ProcessStartedAtUtc,
            ProcessGroupId = identity.ProcessGroupId,
            FencingKind = identity.FencingKind,
            ExecutionStartedAtUtc = identity.ExecutionStartedAtUtc ?? clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<Workspace> UpdateRecoveryStateAsync(Workspace workspace, WorkspaceRecoveryState recoveryState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(recoveryState);
        if (!IsKnownRecoveryState(recoveryState.State) || recoveryState.LastCompletedAttempt < 0 || recoveryState.NextAttempt < 1)
            throw new WorkspaceRecoveryException("Workspace scheduler recovery state is invalid.");
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        var updated = sidecar with
        {
            SchedulerState = recoveryState.State,
            LastCompletedAttempt = recoveryState.LastCompletedAttempt,
            NextAttempt = recoveryState.NextAttempt,
            RetryNotBeforeUtc = recoveryState.RetryNotBeforeUtc,
            RecoveryReason = Bound(recoveryState.Reason),
            UpdatedAtUtc = clock.UtcNow
        };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<Workspace> UpdatePublicationStateAsync(Workspace workspace, PublicationRecoveryState publicationState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(publicationState);
        if (publicationState.State is not (PublicationStates.None or PublicationStates.Authorized or PublicationStates.Committed or PublicationStates.Pushed or PublicationStates.PullRequestCreated))
            throw new WorkspaceRecoveryException("Workspace publication recovery state is invalid.");
        ValidatePublicationState(publicationState.State, publicationState.CommitSha, publicationState.RemoteBranch, publicationState.PullRequestNumber, publicationState.PullRequestUrl, publicationState.Revision, publicationState.AuthorizedParentSha);
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        var currentRank = PublicationRank(sidecar.PublicationState);
        var requestedRank = PublicationRank(publicationState.State);
        if (publicationState.Revision == sidecar.PublicationRevision && publicationState.State == PublicationStates.Authorized && currentRank >= requestedRank)
            return ToWorkspace(sidecar);
        var beginsRevision = publicationState.Revision == sidecar.PublicationRevision + 1
            && sidecar.PublicationState == PublicationStates.PullRequestCreated
            && publicationState.State == PublicationStates.Authorized
            && string.Equals(publicationState.AuthorizedParentSha, sidecar.PublicationCommitSha, StringComparison.OrdinalIgnoreCase);
        if (!beginsRevision && (publicationState.Revision != sidecar.PublicationRevision || requestedRank != currentRank + 1))
            throw new WorkspaceRecoveryException("Workspace publication state transition is not authorized.");
        var updated = sidecar with
        {
            PublicationState = publicationState.State,
            PublicationCommitSha = publicationState.CommitSha,
            PublicationRemoteBranch = publicationState.RemoteBranch,
            PullRequestNumber = publicationState.PullRequestNumber,
            PullRequestUrl = Bound(publicationState.PullRequestUrl),
            PublicationRevision = publicationState.Revision,
            AuthorizedParentSha = publicationState.AuthorizedParentSha,
            CiStatus = beginsRevision ? null : sidecar.CiStatus,
            CiHeadSha = beginsRevision ? null : sidecar.CiHeadSha,
            CiRemediationRound = beginsRevision ? 0 : sidecar.CiRemediationRound,
            CiObservedAtUtc = beginsRevision ? null : sidecar.CiObservedAtUtc,
            CiFailureReason = beginsRevision ? null : sidecar.CiFailureReason,
            ReviewFeedbackStatus = beginsRevision ? null : sidecar.ReviewFeedbackStatus,
            ReviewFeedbackHeadSha = beginsRevision ? null : sidecar.ReviewFeedbackHeadSha,
            ReviewFeedbackRemediationRound = beginsRevision ? 0 : sidecar.ReviewFeedbackRemediationRound,
            ReviewFeedbackObservedAtUtc = beginsRevision ? null : sidecar.ReviewFeedbackObservedAtUtc,
            UpdatedAtUtc = clock.UtcNow
        };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<Workspace> ReconcileAuthorizedWithoutCommitAsync(Workspace workspace, PublicationRecoveryState authoritativeState, CancellationToken cancellationToken)
    {
        if (authoritativeState.State != PublicationStates.PullRequestCreated || authoritativeState.CommitSha is null
            || authoritativeState.Revision < 1 || authoritativeState.PullRequestNumber is not > 0 || authoritativeState.PullRequestUrl is null)
            throw new WorkspaceRecoveryException("Authorized publication reconciliation state is invalid.");
        var path = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(path, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        if (sidecar.PublicationState != PublicationStates.Authorized || sidecar.PublicationCommitSha is not null
            || sidecar.PublicationRevision != authoritativeState.Revision + 1
            || !string.Equals(sidecar.AuthorizedParentSha, authoritativeState.CommitSha, StringComparison.OrdinalIgnoreCase)
            || sidecar.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.None)
            throw new WorkspaceRecoveryException("Authorized publication reconciliation is not safe.");
        var updated = sidecar with { PublicationState = authoritativeState.State, PublicationCommitSha = authoritativeState.CommitSha,
            PublicationRemoteBranch = authoritativeState.RemoteBranch, PullRequestNumber = authoritativeState.PullRequestNumber,
            PullRequestUrl = Bound(authoritativeState.PullRequestUrl), PublicationRevision = authoritativeState.Revision,
            AuthorizedParentSha = authoritativeState.AuthorizedParentSha, UpdatedAtUtc = clock.UtcNow };
        await WriteSidecarAsync(path, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<Workspace> UpdateCiStateAsync(Workspace workspace, CiRecoveryState ciState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(ciState);
        if (ciState.Status is not (CiStatuses.Pending or CiStatuses.Passed or CiStatuses.Failed or CiStatuses.Indeterminate)
            || !IsSha(ciState.HeadSha) || ciState.RemediationRound < 0)
            throw new WorkspaceRecoveryException("Workspace CI recovery state is invalid.");
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        if (sidecar.PublicationState != PublicationStates.PullRequestCreated
            || !string.Equals(sidecar.PublicationCommitSha, ciState.HeadSha, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceRecoveryException("CI state must bind to the exact durable publication head.");
        var updated = sidecar with { CiStatus = ciState.Status, CiHeadSha = ciState.HeadSha, CiRemediationRound = ciState.RemediationRound, CiObservedAtUtc = ciState.LastObservedAtUtc, CiFailureReason = Bound(ciState.FailureReason), UpdatedAtUtc = clock.UtcNow };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<Workspace> UpdateReviewFeedbackStateAsync(Workspace workspace, ReviewFeedbackRecoveryState feedbackState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(feedbackState);
        if (feedbackState.Status is not (ReviewFeedbackStatuses.AwaitingHuman or ReviewFeedbackStatuses.Actionable or ReviewFeedbackStatuses.Approved)
            || !IsSha(feedbackState.HeadSha) || feedbackState.RemediationRound < 0)
            throw new WorkspaceRecoveryException("Workspace review feedback recovery state is invalid.");
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        if (sidecar.PublicationState != PublicationStates.PullRequestCreated
            || !string.Equals(sidecar.PublicationCommitSha, feedbackState.HeadSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sidecar.CiStatus, CiStatuses.Passed, StringComparison.Ordinal)
            || !string.Equals(sidecar.CiHeadSha, feedbackState.HeadSha, StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceRecoveryException("Review feedback state must bind to an exact durable publication head that passed CI.");
        var consumed = (feedbackState.Consumed ?? sidecar.ConsumedFeedback ?? [])
            .GroupBy(x => $"{x.RepositoryIdentity}\u001f{x.PullRequestNumber}\u001f{x.FeedbackId}\u001f{x.Version}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last()).TakeLast(64).ToArray();
        var pending = (feedbackState.Pending ?? sidecar.PendingFeedback ?? []).TakeLast(64).ToArray();
        var updated = sidecar with { ReviewFeedbackStatus = feedbackState.Status, ReviewFeedbackHeadSha = feedbackState.HeadSha, ReviewFeedbackRemediationRound = feedbackState.RemediationRound, ReviewFeedbackObservedAtUtc = feedbackState.LastObservedAtUtc, ConsumedFeedback = consumed, PendingFeedback = pending, UpdatedAtUtc = clock.UtcNow };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
    }

    public async Task<ReviewFeedbackRecoveryState?> GetReviewFeedbackStateAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var sidecar = await ReadSidecarAsync(GetSidecarPath(workspace.Key), cancellationToken);
        return sidecar?.ReviewFeedbackStatus is null || sidecar.ReviewFeedbackHeadSha is null ? null
            : new ReviewFeedbackRecoveryState(sidecar.ReviewFeedbackStatus, sidecar.ReviewFeedbackHeadSha, sidecar.ReviewFeedbackRemediationRound, sidecar.ReviewFeedbackObservedAtUtc, sidecar.ConsumedFeedback, sidecar.PendingFeedback);
    }

    public async Task<IReadOnlyList<OwnedWorkspaceRecord>> EnumerateOwnedWorkspacesAsync(WorkspaceRepository repository, CancellationToken cancellationToken)
    {
        repository = repository with { RootPath = Path.GetFullPath(repository.RootPath) };
        ValidateRepository(repository);
        var sidecarRoot = SidecarRoot();
        if (!Directory.Exists(sidecarRoot)) return [];
        EnsureNoReparsePoints(sidecarRoot);
        var records = new List<OwnedWorkspaceRecord>();
        foreach (var path in Directory.EnumerateFiles(sidecarRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureContained(path, sidecarRoot);
            var sidecar = await ReadSidecarAsync(path, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar disappeared during enumeration.");
            sidecar = await MigrateSidecarAsync(path, sidecar, cancellationToken);
            var key = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(key, CreateKey(sidecar.WorkItemId), StringComparison.Ordinal))
                throw new WorkspaceRecoveryException("Workspace sidecar key does not match its stable work item identity.");
            var expectedPath = GetWorkspacePath(key);
            var request = new WorkspaceRequest(new WorkItem(sidecar.WorkItemId, sidecar.WorkItemId, string.Empty, null, string.Empty, []), repository);
            var expectedBranch = $"automaton/work/{key}";
            ValidateSidecar(sidecar, request, key, expectedPath, expectedBranch);
            if (Directory.Exists(expectedPath))
            {
                EnsureNoReparsePoints(expectedPath);
                await EnsureRegisteredWorktreeAsync(repository.RootPath, expectedPath, expectedBranch, cancellationToken);
            }
            records.Add(ToRecoveryRecord(sidecar));
        }
        return records;
    }

    public async Task CleanupTerminalAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var sidecarPath = GetSidecarPath(workspace.Key);
        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken) ?? throw new WorkspaceRecoveryException("Workspace sidecar state is missing.");
        ValidateWorkspaceMatchesSidecar(workspace, sidecar);
        if (!string.Equals(sidecar.LifecycleStatus, WorkspaceLifecycleStates.Terminal, StringComparison.Ordinal))
            throw new WorkspaceException("Only terminal daemon-owned workspaces are eligible for cleanup.");
        if (!string.Equals(sidecar.ExecutionOwnershipStatus, WorkspaceExecutionOwnershipStates.None, StringComparison.Ordinal))
            throw new WorkspaceOwnershipException("Workspace with unresolved execution ownership cannot be cleaned.");
        ValidateRepository(new WorkspaceRepository(sidecar.RepositoryRootPath, sidecar.RepositoryIdentity));
        EnsureContained(workspace.Path, workspaceRoot);
        EnsureNoReparsePoints(workspace.Path);
        await EnsureRegisteredWorktreeAsync(sidecar.RepositoryRootPath, workspace.Path, sidecar.Branch, cancellationToken);

        var result = await git.RunAsync(sidecar.RepositoryRootPath, ["worktree", "remove", "--force", workspace.Path], cancellationToken);
        EnsureGitSuccess(result, "remove the workspace worktree");
        File.Delete(sidecarPath);
    }

    public static string CreateKey(string stableWorkItemId)
    {
        if (string.IsNullOrWhiteSpace(stableWorkItemId)) throw new WorkspaceException("A stable work item ID is required for workspace creation.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stableWorkItemId));
        return $"work-{Convert.ToHexString(hash).ToLowerInvariant()[..32]}";
    }

    private async Task<Workspace> CreateWorktreeAsync(WorkspaceRequest request, WorkspaceSidecar sidecar, string sidecarPath, CancellationToken cancellationToken)
    {
        await WriteSidecarAsync(sidecarPath, sidecar, cancellationToken);
        var branchExists = await BranchExistsAsync(request.Repository.RootPath, sidecar.Branch, cancellationToken);
        var arguments = branchExists
            ? new[] { "worktree", "add", "--force", sidecar.WorkspacePath, sidecar.Branch }
            : new[] { "worktree", "add", "-b", sidecar.Branch, sidecar.WorkspacePath, sidecar.BaseCommitSha! };
        var result = await git.RunAsync(request.Repository.RootPath, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            await WriteSidecarAsync(sidecarPath, sidecar with { LifecycleStatus = WorkspaceLifecycleStates.Failed, UpdatedAtUtc = clock.UtcNow }, cancellationToken);
            throw new WorkspaceException("Git could not create the isolated workspace.");
        }
        var ready = sidecar with { LifecycleStatus = WorkspaceLifecycleStates.Ready, UpdatedAtUtc = clock.UtcNow };
        await WriteSidecarAsync(sidecarPath, ready, cancellationToken);
        return ToWorkspace(ready);
    }

    private async Task<bool> BranchExistsAsync(string repositoryRoot, string branch, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(repositoryRoot, ["show-ref", "--verify", "--quiet", $"refs/heads/{branch}"], cancellationToken);
        if (result.ExitCode is 0 or 1) return result.ExitCode == 0;
        throw new WorkspaceException("Git could not verify the workspace branch.");
    }

    private async Task EnsureRegisteredWorktreeAsync(string repositoryRoot, string workspacePath, string expectedBranch, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(repositoryRoot, ["worktree", "list", "--porcelain"], cancellationToken);
        EnsureGitSuccess(result, "verify the workspace worktree");
        var blocks = result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var registeredPath = lines.FirstOrDefault(line => line.StartsWith("worktree ", StringComparison.Ordinal));
            if (registeredPath is null || !PathsEqual(registeredPath[9..].Trim(), workspacePath)) continue;
            var registeredBranch = lines.FirstOrDefault(line => line.StartsWith("branch ", StringComparison.Ordinal));
            if (string.Equals(registeredBranch, $"branch refs/heads/{expectedBranch}", StringComparison.Ordinal)) return;
            throw new WorkspaceRecoveryException("Workspace is detached or registered to a different branch and will not be reused or removed.");
        }
        throw new WorkspaceRecoveryException("Workspace is not a registered Git worktree and will not be reused or removed.");
    }

    private async Task<WorkspaceSidecar?> ReadSidecarAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        EnsureContained(path, SidecarRoot());
        EnsureNoReparsePoints(path);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<WorkspaceSidecar>(stream, cancellationToken: cancellationToken)
                ?? throw new WorkspaceRecoveryException("Workspace sidecar state is empty.");
        }
        catch (JsonException)
        {
            throw new WorkspaceRecoveryException("Workspace sidecar state is invalid.");
        }
    }

    private async Task<WorkspaceSidecar> MigrateSidecarAsync(string path, WorkspaceSidecar sidecar, CancellationToken cancellationToken)
    {
        if (sidecar.SchemaVersion > CurrentSchemaVersion)
            throw new WorkspaceRecoveryException("Workspace sidecar schema version is newer than this daemon supports.");
        if (sidecar.SchemaVersion == CurrentSchemaVersion) return sidecar;
        if (sidecar.SchemaVersion is not (0 or 2 or 3 or 4 or 5))
            throw new WorkspaceRecoveryException("Workspace sidecar schema version is unsupported.");
        var migrated = sidecar.SchemaVersion is 2 or 3 or 4 or 5
            ? sidecar with
            {
                SchemaVersion = CurrentSchemaVersion,
                PublicationState = (sidecar.SchemaVersion == 4
                    && sidecar.PublicationState is PublicationStates.Authorized or PublicationStates.Committed or PublicationStates.Pushed or PublicationStates.PullRequestCreated)
                    || (sidecar.SchemaVersion == 3
                    && sidecar.PublicationState is PublicationStates.Committed or PublicationStates.Pushed or PublicationStates.PullRequestCreated)
                        ? sidecar.PublicationState
                        : PublicationStates.None,
                PublicationRevision = 1,
                AuthorizedParentSha = sidecar.SchemaVersion == 4 && sidecar.PublicationState == PublicationStates.Authorized
                    || sidecar.PublicationState is PublicationStates.Committed or PublicationStates.Pushed or PublicationStates.PullRequestCreated ? sidecar.BaseCommitSha : null,
                UpdatedAtUtc = clock.UtcNow
            }
            : sidecar with
        {
            SchemaVersion = CurrentSchemaVersion,
            SchedulerState = WorkspaceRecoveryStates.Blocked,
            NextAttempt = Math.Max(1, sidecar.NextAttempt),
            RecoveryReason = "Legacy sidecar requires conservative manual recovery.",
            ExecutionOwnershipStatus = sidecar.ExecutionOwnershipStatus == WorkspaceExecutionOwnershipStates.Active
                ? WorkspaceExecutionOwnershipStates.Quarantined
                : sidecar.ExecutionOwnershipStatus,
            UpdatedAtUtc = clock.UtcNow
        };
        await WriteSidecarAsync(path, migrated, cancellationToken);
        return migrated;
    }

    private async Task WriteSidecarAsync(string path, WorkspaceSidecar sidecar, CancellationToken cancellationToken)
    {
        EnsureContained(path, SidecarRoot());
        Directory.CreateDirectory(SidecarRoot());
        EnsureNoReparsePoints(SidecarRoot());
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, sidecar, json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, path, true);
    }

    private void ValidateRepository(WorkspaceRepository repository)
    {
        if (string.IsNullOrWhiteSpace(repository.Identity)) throw new WorkspaceException("Workspace repository identity is required.");
        if (string.IsNullOrWhiteSpace(repository.BaseBranch) || repository.BaseBranch.Any(char.IsControl)
            || repository.BaseBranch.Contains("..", StringComparison.Ordinal)
            || repository.BaseBranch.IndexOfAny([' ', '~', '^', ':', '?', '*', '[', '\\']) >= 0
            || repository.BaseBranch.StartsWith('/') || repository.BaseBranch.EndsWith('/') || repository.BaseBranch.EndsWith('.'))
            throw new WorkspaceException("Configured repository base branch is invalid.");
        if (string.IsNullOrWhiteSpace(repository.RootPath) || !Directory.Exists(repository.RootPath))
            throw new WorkspaceException("Configured canonical repository root does not exist.");
        var repositoryRoot = Path.GetFullPath(repository.RootPath);
        EnsureNoReparsePoints(repositoryRoot);
        if (IsContainedBy(repositoryRoot, workspaceRoot) || IsContainedBy(workspaceRoot, repositoryRoot))
            throw new WorkspaceException("Canonical repository root and workspace root must be disjoint.");
    }

    private void ValidateSidecar(WorkspaceSidecar sidecar, WorkspaceRequest request, string key, string workspacePath, string branch)
    {
        if (!string.Equals(sidecar.WorkItemId, request.WorkItem.Id, StringComparison.Ordinal)
            || !string.Equals(sidecar.RepositoryIdentity, request.Repository.Identity, StringComparison.Ordinal)
            || (sidecar.BaseBranch is not null && !string.Equals(sidecar.BaseBranch, request.Repository.BaseBranch, StringComparison.Ordinal))
            || !PathsEqual(sidecar.RepositoryRootPath, request.Repository.RootPath)
            || !string.Equals(sidecar.WorkspaceKey, key, StringComparison.Ordinal)
            || !PathsEqual(sidecar.WorkspacePath, workspacePath)
            || !string.Equals(sidecar.Branch, branch, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(sidecar.LaunchNonce)
            || sidecar.SchemaVersion != CurrentSchemaVersion
            || !IsKnownRecoveryState(sidecar.SchedulerState)
            || sidecar.LastCompletedAttempt < 0
            || sidecar.NextAttempt < 1
            || (sidecar.BaseCommitSha is not null && !IsSha(sidecar.BaseCommitSha))
            || sidecar.PublicationState is not (PublicationStates.None or PublicationStates.Authorized or PublicationStates.Committed or PublicationStates.Pushed or PublicationStates.PullRequestCreated)
            || (sidecar.CiStatus is not null && sidecar.CiStatus is not (CiStatuses.Pending or CiStatuses.Passed or CiStatuses.Failed or CiStatuses.Indeterminate))
            || (sidecar.CiStatus is not null && (!IsSha(sidecar.CiHeadSha) || sidecar.CiRemediationRound < 0 || sidecar.PublicationState != PublicationStates.PullRequestCreated || !string.Equals(sidecar.CiHeadSha, sidecar.PublicationCommitSha, StringComparison.OrdinalIgnoreCase)))
            || (sidecar.ReviewFeedbackStatus is not null && sidecar.ReviewFeedbackStatus is not (ReviewFeedbackStatuses.AwaitingHuman or ReviewFeedbackStatuses.Actionable or ReviewFeedbackStatuses.Approved))
            || (sidecar.ReviewFeedbackStatus is not null && (!IsSha(sidecar.ReviewFeedbackHeadSha) || sidecar.ReviewFeedbackRemediationRound < 0
                || sidecar.PublicationState != PublicationStates.PullRequestCreated
                || !string.Equals(sidecar.ReviewFeedbackHeadSha, sidecar.PublicationCommitSha, StringComparison.OrdinalIgnoreCase)
                || sidecar.CiStatus != CiStatuses.Passed
                || !string.Equals(sidecar.ReviewFeedbackHeadSha, sidecar.CiHeadSha, StringComparison.OrdinalIgnoreCase)))
            || !IsKnownLifecycleStatus(sidecar.LifecycleStatus)
            || !IsKnownOwnershipStatus(sidecar.ExecutionOwnershipStatus))
            throw new WorkspaceRecoveryException("Workspace sidecar state does not match the requested daemon-owned workspace.");
        ValidatePublicationState(sidecar.PublicationState, sidecar.PublicationCommitSha, sidecar.PublicationRemoteBranch, sidecar.PullRequestNumber, sidecar.PullRequestUrl, sidecar.PublicationRevision, sidecar.AuthorizedParentSha);
        EnsureContained(sidecar.WorkspacePath, workspaceRoot);
    }

    private static void ValidatePublicationState(string state, string? commitSha, string? remoteBranch, int? pullRequestNumber, string? pullRequestUrl, int revision, string? authorizedParentSha)
    {
        if (revision < 1 || (authorizedParentSha is not null && !IsSha(authorizedParentSha)))
            throw new WorkspaceRecoveryException("Workspace publication revision metadata is invalid.");
        if (state is PublicationStates.None or PublicationStates.Authorized)
        {
            if (commitSha is not null || (state == PublicationStates.None && (remoteBranch is not null || pullRequestNumber is not null || pullRequestUrl is not null || authorizedParentSha is not null)))
                throw new WorkspaceRecoveryException("Pre-commit publication state contains unexpected recovery metadata.");
            if (state == PublicationStates.Authorized && authorizedParentSha is null)
                throw new WorkspaceRecoveryException("Publication authorization is not bound to an expected parent SHA.");
            if (state == PublicationStates.Authorized && revision > 1
                && (string.IsNullOrWhiteSpace(remoteBranch) || pullRequestNumber is null or <= 0))
                throw new WorkspaceRecoveryException("CI remediation authorization is not bound to the existing draft pull request.");
            return;
        }
        if (authorizedParentSha is null || commitSha is null || commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(remoteBranch))
            throw new WorkspaceRecoveryException("Workspace publication recovery metadata is invalid.");
        if (state == PublicationStates.PullRequestCreated
            && (pullRequestNumber is null or <= 0 || !Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out _)))
            throw new WorkspaceRecoveryException("Workspace pull-request recovery metadata is invalid.");
        if (revision > 1 && (pullRequestNumber is null or <= 0))
            throw new WorkspaceRecoveryException("CI remediation publication lost its draft pull-request identity.");
        if (revision == 1 && state != PublicationStates.PullRequestCreated && (pullRequestNumber is not null || pullRequestUrl is not null))
            throw new WorkspaceRecoveryException("Workspace publication phase contains premature pull-request metadata.");
    }

    private static int PublicationRank(string state) => state switch
    {
        PublicationStates.None => 0,
        PublicationStates.Authorized => 1,
        PublicationStates.Committed => 2,
        PublicationStates.Pushed => 3,
        PublicationStates.PullRequestCreated => 4,
        _ => throw new WorkspaceRecoveryException("Workspace publication recovery state is invalid.")
    };

    private void ValidateWorkspaceMatchesSidecar(Workspace workspace, WorkspaceSidecar sidecar)
    {
        var mismatches = new (string Name, string? Supplied, string? Durable, bool Equal)[]
        {
            ("Key", workspace.Key, sidecar.WorkspaceKey, string.Equals(workspace.Key, sidecar.WorkspaceKey, StringComparison.Ordinal)),
            ("Path", workspace.Path, sidecar.WorkspacePath, PathsEqual(workspace.Path, sidecar.WorkspacePath)),
            ("Branch", workspace.Branch, sidecar.Branch, string.Equals(workspace.Branch, sidecar.Branch, StringComparison.Ordinal)),
            ("ExecutionOwnershipStatus", workspace.ExecutionOwnershipStatus, sidecar.ExecutionOwnershipStatus, string.Equals(workspace.ExecutionOwnershipStatus, sidecar.ExecutionOwnershipStatus, StringComparison.Ordinal)),
            ("RepositoryIdentity", workspace.RepositoryIdentity, sidecar.RepositoryIdentity, string.Equals(workspace.RepositoryIdentity, sidecar.RepositoryIdentity, StringComparison.Ordinal)),
            ("BaseCommitSha", workspace.BaseCommitSha, sidecar.BaseCommitSha, string.Equals(workspace.BaseCommitSha, sidecar.BaseCommitSha, StringComparison.Ordinal)),
            ("LaunchNonce", workspace.LaunchNonce, sidecar.LaunchNonce, string.Equals(workspace.LaunchNonce, sidecar.LaunchNonce, StringComparison.Ordinal))
        };
        var mismatchIndex = Array.FindIndex(mismatches, value => !value.Equal);
        if (mismatchIndex >= 0)
        {
            var mismatch = mismatches[mismatchIndex];
            throw new WorkspaceRecoveryException($"Workspace does not match daemon-owned sidecar state during workspace validation: field={mismatch.Name}; supplied={mismatch.Supplied}; durable={mismatch.Durable}.");
        }
        EnsureContained(workspace.Path, workspaceRoot);
    }

    private string GetWorkspacePath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(workspaceRoot, key));
        EnsureContained(path, workspaceRoot);
        return path;
    }

    private string GetSidecarPath(string key)
    {
        var path = Path.GetFullPath(Path.Combine(SidecarRoot(), key + ".json"));
        EnsureContained(path, SidecarRoot());
        return path;
    }

    private string SidecarRoot() => Path.Combine(workspaceRoot, SidecarDirectoryName);
    private static Workspace ToWorkspace(WorkspaceSidecar sidecar)
        => new(sidecar.WorkspaceKey, sidecar.WorkspacePath, sidecar.Branch, sidecar.LifecycleStatus, sidecar.ExecutionOwnershipStatus, sidecar.LaunchNonce, sidecar.RepositoryIdentity, sidecar.BaseCommitSha);
    private static OwnedWorkspaceRecord ToRecoveryRecord(WorkspaceSidecar sidecar)
        => new(
            ToWorkspace(sidecar),
            sidecar.WorkItemId,
            new WorkspaceRepository(sidecar.RepositoryRootPath, sidecar.RepositoryIdentity, sidecar.BaseBranch ?? "main"),
            new WorkspaceRecoveryState(sidecar.SchedulerState, sidecar.LastCompletedAttempt, sidecar.NextAttempt, sidecar.RetryNotBeforeUtc, sidecar.RecoveryReason),
            sidecar.ProcessId is { } processId && sidecar.ProcessStartedAtUtc is { } started && sidecar.FencingKind is { } fencing
                ? new DurableExecutionIdentity(processId, started, sidecar.LaunchNonce, fencing, sidecar.ProcessGroupId, sidecar.ExecutionStartedAtUtc)
                : null,
            sidecar.SchemaVersion,
            new PublicationRecoveryState(sidecar.PublicationState, sidecar.PublicationCommitSha, sidecar.PublicationRemoteBranch, sidecar.PullRequestNumber, sidecar.PullRequestUrl, sidecar.PublicationRevision, sidecar.AuthorizedParentSha),
            sidecar.CiStatus is null || sidecar.CiHeadSha is null ? null : new CiRecoveryState(sidecar.CiStatus, sidecar.CiHeadSha, sidecar.CiRemediationRound, sidecar.CiObservedAtUtc, sidecar.CiFailureReason),
            sidecar.ReviewFeedbackStatus is null || sidecar.ReviewFeedbackHeadSha is null ? null : new ReviewFeedbackRecoveryState(sidecar.ReviewFeedbackStatus, sidecar.ReviewFeedbackHeadSha, sidecar.ReviewFeedbackRemediationRound, sidecar.ReviewFeedbackObservedAtUtc, sidecar.ConsumedFeedback, sidecar.PendingFeedback));

    private async Task<string> ResolveBaseShaAsync(string repositoryRoot, string baseBranch, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(repositoryRoot, ["rev-parse", $"refs/heads/{baseBranch}"], cancellationToken);
        EnsureGitSuccess(result, "resolve the workspace base commit");
        var sha = result.StandardOutput.Trim();
        if (!IsSha(sha)) throw new WorkspaceRecoveryException("Git returned an invalid workspace base commit.");
        return sha;
    }

    private static bool IsSha(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    private static void EnsureGitSuccess(GitCommandResult result, string operation)
    {
        if (result.ExitCode != 0) throw new WorkspaceException($"Git could not {operation}.");
    }

    private static string RequireRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new WorkspaceException("workspace.root must be configured.");
        return Path.GetFullPath(root);
    }

    private static bool IsKnownLifecycleStatus(string status) => status is WorkspaceLifecycleStates.Preparing or WorkspaceLifecycleStates.Ready
        or WorkspaceLifecycleStates.RetryableFailure or WorkspaceLifecycleStates.Failed or WorkspaceLifecycleStates.Terminal
        ;
    private static bool IsKnownOwnershipStatus(string status) => status is WorkspaceExecutionOwnershipStates.None
        or WorkspaceExecutionOwnershipStates.Active or WorkspaceExecutionOwnershipStates.Quarantined;
    private static bool IsKnownRecoveryState(string status) => status is WorkspaceRecoveryStates.Ready or WorkspaceRecoveryStates.Working
        or WorkspaceRecoveryStates.Completed or WorkspaceRecoveryStates.Blocked or WorkspaceRecoveryStates.Cancelled
        or WorkspaceRecoveryStates.RetryableFailure or WorkspaceRecoveryStates.Ineligible;
    private static string? Bound(string? value) => value is null || value.Length <= 512 ? value : value[..512];

    private static void EnsureContained(string path, string root)
    {
        if (!IsContainedBy(path, root)) throw new WorkspaceException("Workspace path is outside the configured workspace root.");
    }

    private static bool IsContainedBy(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return PathsEqual(fullPath, fullRoot) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), PathComparison);
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void EnsureNoReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        FileSystemInfo? current = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath)
            : File.Exists(fullPath)
                ? new FileInfo(fullPath)
                : new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? fullPath);
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceException("Workspace paths containing reparse points are not supported.");
            current = current switch
            {
                DirectoryInfo directory => directory.Parent,
                FileInfo file => file.Directory,
                _ => null
            };
        }
    }

    private sealed record WorkspaceSidecar(
        string WorkspaceKey,
        string WorkItemId,
        string RepositoryIdentity,
        string RepositoryRootPath,
        string WorkspacePath,
        string Branch,
        string LifecycleStatus,
        string ExecutionOwnershipStatus,
        string LaunchNonce,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        int SchemaVersion = 0,
        string SchedulerState = WorkspaceRecoveryStates.Ready,
        int LastCompletedAttempt = 0,
        int NextAttempt = 1,
        DateTimeOffset? RetryNotBeforeUtc = null,
        string? RecoveryReason = null,
        int? ProcessId = null,
        DateTimeOffset? ProcessStartedAtUtc = null,
        int? ProcessGroupId = null,
        string? FencingKind = null,
        DateTimeOffset? ExecutionStartedAtUtc = null,
        string? BaseCommitSha = null,
        string PublicationState = PublicationStates.None,
        string? PublicationCommitSha = null,
        string? PublicationRemoteBranch = null,
        int? PullRequestNumber = null,
        string? PullRequestUrl = null,
        string? BaseBranch = null,
        int PublicationRevision = 1,
        string? AuthorizedParentSha = null,
        string? CiStatus = null,
        string? CiHeadSha = null,
        int CiRemediationRound = 0,
        DateTimeOffset? CiObservedAtUtc = null,
        string? CiFailureReason = null,
        string? ReviewFeedbackStatus = null,
        string? ReviewFeedbackHeadSha = null,
        int ReviewFeedbackRemediationRound = 0,
        DateTimeOffset? ReviewFeedbackObservedAtUtc = null,
        IReadOnlyList<ConsumedReviewFeedback>? ConsumedFeedback = null,
        IReadOnlyList<PendingReviewFeedback>? PendingFeedback = null);
}

public class WorkspaceException(string message) : Exception(message);
public sealed class WorkspaceRecoveryException(string message) : WorkspaceException(message);
public sealed class WorkspaceOwnershipException(string message) : WorkspaceException(message);
