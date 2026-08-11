using System.Diagnostics;
using System.Text;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;
using Automaton.AgentOrchestrator.Workspaces;

namespace Automaton.AgentOrchestrator.Publication;

public sealed class PublicationException(string message, bool isRetryable = false) : Exception(message)
{
    public bool IsRetryable { get; } = isRetryable;
}

public sealed class GitWorkPublisher(
    IGitCommandRunner git,
    IWorkspaceManager workspaces,
    IPullRequestPublisher pullRequests,
    OrchestratorOptions options) : IWorkPublisher
{
    private const int MaximumSubjectLength = 120;
    private const int MaximumBodyLength = 12_000;

    public async Task<PublicationResult> PublishAsync(PublicationRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Reviewer.Verdict, RoleResultStatuses.Approved, StringComparison.Ordinal))
            throw new PublicationException("Durable publication authorization requires independent Reviewer approval.");
        var workspace = request.Workspace;
        ValidateWorkspaceForPublication(workspace, requireActiveOwnership: true);
        workspace = await workspaces.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.Authorized), CancellationToken.None);

        await ValidateGitStateAsync(workspace, cancellationToken);
        await RunValidationCommandsAsync(workspace, cancellationToken);
        var subject = BuildSubject(request.WorkItem);
        await ValidateRemoteAsync(workspace, cancellationToken);
        var commitSha = await PrepareCommitAsync(workspace, subject, cancellationToken);
        workspace = await workspaces.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.Committed, commitSha, workspace.Branch), CancellationToken.None);

        await PublishBranchAsync(workspace, commitSha, cancellationToken);
        workspace = await workspaces.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.Pushed, commitSha, workspace.Branch), CancellationToken.None);

        var publication = await pullRequests.FindOrCreateAsync(new(
            options.Repository.Identity,
            workspace.Branch,
            commitSha,
            options.Repository.BaseBranch,
            subject,
            BuildPullRequestBody(request)), cancellationToken);
        ValidatePullRequest(publication, workspace, commitSha, options.Repository.BaseBranch);
        await workspaces.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.PullRequestCreated, commitSha, workspace.Branch, publication.Number, publication.Url), CancellationToken.None);
        return new(options.Repository.Identity, workspace.Branch, commitSha, publication.Number, publication.Url, publication.Draft);
    }

    public async Task<PublicationRecoveryResult> RecoverAsync(OwnedWorkspaceRecord record, WorkItem workItem, CancellationToken cancellationToken)
    {
        var workspace = record.Workspace;
        if (workspace.ExecutionOwnershipStatus != WorkspaceExecutionOwnershipStates.None)
            throw new PublicationException("Publication recovery requires execution ownership to be safely released.");
        ValidateWorkspaceForPublication(workspace, requireActiveOwnership: false);
        var recoveryState = record.Publication?.State ?? PublicationStates.None;
        if (recoveryState == PublicationStates.None) return new(false);
        await ValidateGitStateAsync(workspace, cancellationToken);
        var subject = BuildSubject(workItem);
        await RunValidationCommandsAsync(workspace, cancellationToken);
        await ValidateRemoteAsync(workspace, cancellationToken);
        string commitSha;
        if (recoveryState == PublicationStates.Authorized)
        {
            commitSha = await PrepareCommitAsync(workspace, subject, cancellationToken);
            workspace = await workspaces.UpdatePublicationStateAsync(workspace,
                new PublicationRecoveryState(PublicationStates.Committed, commitSha, workspace.Branch), CancellationToken.None);
        }
        else
        {
            commitSha = await RecognizeExistingCommitAsync(workspace, subject, cancellationToken)
                ?? throw new PublicationException("Durable publication state exists but the daemon publication commit cannot be proven.");
            ValidateRecoveryHint(record.Publication, workspace, commitSha);
        }

        var request = new PullRequestPublicationRequest(
            options.Repository.Identity,
            workspace.Branch,
            commitSha,
            options.Repository.BaseBranch,
            subject,
            BuildRecoveryPullRequestBody(workItem));

        PullRequestPublication publication;
        if (record.Publication?.State == PublicationStates.PullRequestCreated)
        {
            await VerifyRemoteBranchAsync(workspace, commitSha, cancellationToken);
            publication = await pullRequests.FindExactAsync(request, record.Publication.PullRequestNumber, cancellationToken)
                ?? throw new PublicationException("The durably recorded draft pull request no longer exists or matches publication identity.");
        }
        else
        {
            if (record.Publication?.State == PublicationStates.Pushed)
                await VerifyRemoteBranchAsync(workspace, commitSha, cancellationToken);
            else
                await PublishBranchAsync(workspace, commitSha, cancellationToken);
            if (record.Publication?.State != PublicationStates.Pushed)
                workspace = await workspaces.UpdatePublicationStateAsync(workspace,
                    new PublicationRecoveryState(PublicationStates.Pushed, commitSha, workspace.Branch), CancellationToken.None);
            publication = await pullRequests.FindOrCreateAsync(request, cancellationToken);
        }

        ValidatePullRequest(publication, workspace, commitSha, options.Repository.BaseBranch);
        await workspaces.UpdatePublicationStateAsync(workspace,
            new PublicationRecoveryState(PublicationStates.PullRequestCreated, commitSha, workspace.Branch, publication.Number, publication.Url), CancellationToken.None);
        return new(true, new(options.Repository.Identity, workspace.Branch, commitSha, publication.Number, publication.Url, publication.Draft));
    }

    private void ValidateWorkspaceForPublication(Workspace workspace, bool requireActiveOwnership)
    {
        var expectedOwnership = requireActiveOwnership ? WorkspaceExecutionOwnershipStates.Active : WorkspaceExecutionOwnershipStates.None;
        if (workspace.ExecutionOwnershipStatus != expectedOwnership)
            throw new PublicationException(requireActiveOwnership
                ? "Publication requires verified active workspace ownership."
                : "Publication recovery requires execution ownership to be safely released.");
        if (!string.Equals(workspace.RepositoryIdentity, options.Repository.Identity, StringComparison.Ordinal))
            throw new PublicationException("Workspace repository identity does not match publication configuration.");
        if (string.IsNullOrWhiteSpace(workspace.BaseCommitSha))
            throw new PublicationException("Workspace base commit provenance is unavailable for safe publication.");
        _ = RequireSha(workspace.BaseCommitSha);
        if (PathsEqual(workspace.Path, options.Repository.Root))
            throw new PublicationException("Publication cannot mutate the canonical repository checkout.");
    }

    private async Task ValidateGitStateAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var root = await GitAsync(workspace.Path, ["rev-parse", "--show-toplevel"], "resolve the publication worktree", cancellationToken);
        if (!PathsEqual(root.StandardOutput.Trim(), workspace.Path)) throw new PublicationException("Publication workspace root does not match the assigned worktree.");
        var branch = await GitAsync(workspace.Path, ["symbolic-ref", "--quiet", "--short", "HEAD"], "resolve the publication branch", cancellationToken);
        if (!string.Equals(branch.StandardOutput.Trim(), workspace.Branch, StringComparison.Ordinal)) throw new PublicationException("Publication worktree is not on the expected daemon-owned branch.");
        var registered = await GitAsync(options.Repository.Root, ["worktree", "list", "--porcelain"], "verify publication worktree registration", cancellationToken);
        if (!ContainsWorktree(registered.StandardOutput, workspace.Path, workspace.Branch)) throw new PublicationException("Publication worktree registration or branch is invalid.");
        var unmerged = await GitAsync(workspace.Path, ["diff", "--name-only", "--diff-filter=U"], "inspect unmerged paths", cancellationToken);
        if (!string.IsNullOrWhiteSpace(unmerged.StandardOutput)) throw new PublicationException("Publication workspace contains unmerged conflicts.");
        foreach (var marker in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply" })
        {
            var path = await GitAsync(workspace.Path, ["rev-parse", "--git-path", marker], "inspect repository operation state", cancellationToken);
            var fullPath = Path.GetFullPath(Path.IsPathRooted(path.StandardOutput.Trim()) ? path.StandardOutput.Trim() : Path.Combine(workspace.Path, path.StandardOutput.Trim()));
            if (File.Exists(fullPath) || Directory.Exists(fullPath)) throw new PublicationException("Publication workspace has an unresolved Git operation.");
        }
    }

    private async Task<string> PrepareCommitAsync(Workspace workspace, string subject, CancellationToken cancellationToken)
    {
        var head = (await GitAsync(workspace.Path, ["rev-parse", "HEAD"], "resolve publication HEAD", cancellationToken)).StandardOutput.Trim();
        if (!string.Equals(head, workspace.BaseCommitSha, StringComparison.Ordinal))
        {
            return await RecognizeExistingCommitAsync(workspace, subject, cancellationToken)
                ?? throw new PublicationException("Workspace contains unexpected existing commits; publication provenance is ambiguous.");
        }

        var status = await GitAsync(workspace.Path, ["status", "--porcelain=v1", "--untracked-files=all"], "inspect publishable changes", cancellationToken);
        if (string.IsNullOrWhiteSpace(status.StandardOutput)) throw new PublicationException("Reviewer-approved workspace has no changes to publish.");
        await GitAsync(workspace.Path, ["add", "--all"], "stage publication changes", cancellationToken);
        await GitAsync(workspace.Path,
            ["-c", $"user.name={options.Publication.GitName}", "-c", $"user.email={options.Publication.GitEmail}", "commit", "-m", subject],
            "create the daemon publication commit", cancellationToken);
        var committed = await GitAsync(workspace.Path, ["rev-parse", "HEAD"], "resolve publication commit", cancellationToken);
        return RequireSha(committed.StandardOutput.Trim());
    }

    private async Task<string?> RecognizeExistingCommitAsync(Workspace workspace, string subject, CancellationToken cancellationToken)
    {
        var head = (await GitAsync(workspace.Path, ["rev-parse", "HEAD"], "resolve publication HEAD", cancellationToken)).StandardOutput.Trim();
        if (string.Equals(head, workspace.BaseCommitSha, StringComparison.OrdinalIgnoreCase)) return null;
        var count = (await GitAsync(workspace.Path, ["rev-list", "--count", $"{workspace.BaseCommitSha}..HEAD"], "inspect publication history", cancellationToken)).StandardOutput.Trim();
        var provenance = (await GitAsync(workspace.Path, ["log", "-1", "--format=%an%x00%ae%x00%s"], "inspect publication commit provenance", cancellationToken)).StandardOutput.TrimEnd('\r', '\n');
        var expected = $"{options.Publication.GitName}\0{options.Publication.GitEmail}\0{subject}";
        if (count != "1" || !string.Equals(provenance, expected, StringComparison.Ordinal)) return null;
        return RequireSha(head);
    }

    private static void ValidateRecoveryHint(PublicationRecoveryState? hint, Workspace workspace, string commitSha)
    {
        if (hint is null || hint.State == PublicationStates.None) return;
        if (!string.Equals(hint.CommitSha, commitSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(hint.RemoteBranch, workspace.Branch, StringComparison.Ordinal))
            throw new PublicationException("Durable publication metadata does not match the recognized daemon publication commit.");
    }

    private async Task ValidateRemoteAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var result = await GitAsync(workspace.Path, ["remote", "get-url", options.Publication.RemoteName], "resolve publication remote", cancellationToken);
        if (!TryParseGitHubRepository(result.StandardOutput.Trim(), out var host, out var identity)
            || !string.Equals(host, options.Publication.GitHubHost, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity, options.Repository.Identity, StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("Publication remote does not match the configured GitHub repository.");
    }

    private async Task PublishBranchAsync(Workspace workspace, string commitSha, CancellationToken cancellationToken)
    {
        var existing = await ReadRemoteShaAsync(workspace, cancellationToken);
        if (existing is not null && !string.Equals(existing, commitSha, StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("Remote work branch exists at an unexpected commit.");
        if (existing is null)
        {
            var push = await git.RunAsync(workspace.Path,
                ["push", "--porcelain", options.Publication.RemoteName, $"HEAD:refs/heads/{workspace.Branch}"], cancellationToken);
            if (push.ExitCode != 0) throw ClassifyGitNetworkFailure(push.StandardError, "Git could not push the publication branch.");
        }
        var verified = await ReadRemoteShaAsync(workspace, cancellationToken);
        if (!string.Equals(verified, commitSha, StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("Remote branch SHA does not match the publication commit.");
    }

    private async Task VerifyRemoteBranchAsync(Workspace workspace, string commitSha, CancellationToken cancellationToken)
    {
        var remote = await ReadRemoteShaAsync(workspace, cancellationToken);
        if (!string.Equals(remote, commitSha, StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("Durably published remote branch is missing or has an unexpected SHA.");
    }

    private async Task<string?> ReadRemoteShaAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(workspace.Path,
            ["ls-remote", "--heads", options.Publication.RemoteName, $"refs/heads/{workspace.Branch}"], cancellationToken);
        if (result.ExitCode != 0) throw ClassifyGitNetworkFailure(result.StandardError, "Git could not inspect the remote work branch.");
        if (string.IsNullOrWhiteSpace(result.StandardOutput)) return null;
        var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 1) throw new PublicationException("Remote branch lookup returned an ambiguous result.");
        return RequireSha(lines[0].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0]);
    }

    private async Task RunValidationCommandsAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        foreach (var command in options.Publication.ValidationCommands)
        {
            var (fileName, arguments) = ProcessCodexJsonRpcTransport.ParseCommand(command);
            var start = new ProcessStartInfo(fileName) { WorkingDirectory = workspace.Path, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            var inherited = start.Environment.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
            start.Environment.Clear();
            foreach (var name in new[] { "PATH", "PATHEXT", "SYSTEMROOT", "COMSPEC", "TEMP", "TMP", "HOME", "USERPROFILE" })
                if (inherited.TryGetValue(name, out var value) && value is not null) start.Environment[name] = value;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new PublicationException("Publication validation command could not start.", true);
            var stdout = DrainAsync(process.StandardOutput);
            var stderr = DrainAsync(process.StandardError);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Publication.ValidationTimeoutMilliseconds);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                await Task.WhenAll(stdout, stderr);
                throw new PublicationException("Publication validation command timed out.");
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0) throw new PublicationException("Repository publication validation failed.");
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) > 0) { }
    }

    private async Task<GitCommandResult> GitAsync(string cwd, IReadOnlyList<string> arguments, string operation, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(cwd, arguments, cancellationToken);
        if (result.ExitCode != 0) throw new PublicationException($"Git could not {operation}.");
        return result;
    }

    internal static string BuildSubject(WorkItem item)
    {
        var value = Sanitize($"{item.Identifier}: {item.Title}");
        return value.Length <= MaximumSubjectLength ? value : value[..MaximumSubjectLength].TrimEnd();
    }

    private static string BuildPullRequestBody(PublicationRequest request)
    {
        var issueReference = request.WorkItem.Identifier.Contains('#') ? request.WorkItem.Identifier[(request.WorkItem.Identifier.LastIndexOf('#'))..] : request.WorkItem.Identifier;
        var body = $"""
Automaton generated this draft PR for {Sanitize(request.WorkItem.Identifier)}.

Implementation
{Sanitize(request.Developer.Summary)}

Validation
{Sanitize(request.Developer.Validation)}

Independent review
{Sanitize(request.Reviewer.Summary)}

Source
Refs {Sanitize(issueReference)}
""";
        return body.Length <= MaximumBodyLength ? body : body[..MaximumBodyLength];
    }

    private static string BuildRecoveryPullRequestBody(WorkItem item)
    {
        var issueReference = item.Identifier.Contains('#') ? item.Identifier[item.Identifier.LastIndexOf('#')..] : item.Identifier;
        return $"Automaton recovered an independently approved draft publication for {Sanitize(item.Identifier)}.\n\nSource\nRefs {Sanitize(issueReference)}";
    }

    private static string Sanitize(string value) => new string(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
    private static string RequireSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : throw new PublicationException("Git returned an invalid commit SHA.");
    private static bool PathsEqual(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool ContainsWorktree(string porcelain, string path, string branch)
        => porcelain.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Any(block => block.Split('\n').Contains($"worktree {Path.GetFullPath(path)}", StringComparerForPaths)
                          && block.Split('\n').Contains($"branch refs/heads/{branch}", StringComparer.Ordinal));
    private static StringComparer StringComparerForPaths => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static bool TryParseGitHubRepository(string remote, out string host, out string identity)
    {
        host = identity = string.Empty;
        if (remote.StartsWith("git@", StringComparison.OrdinalIgnoreCase) && remote.Contains(':'))
        {
            var separator = remote.IndexOf(':');
            host = remote[4..separator];
            identity = remote[(separator + 1)..];
        }
        else if (Uri.TryCreate(remote, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "ssh")
        {
            if ((uri.Scheme == "https" && uri.UserInfo.Length > 0)
                || (uri.Scheme == "ssh" && uri.UserInfo.Length > 0 && !string.Equals(uri.UserInfo, "git", StringComparison.Ordinal))
                || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
            host = uri.Host;
            identity = uri.AbsolutePath.Trim('/');
        }
        else return false;
        if (identity.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) identity = identity[..^4];
        return identity.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 2;
    }

    private static void ValidatePullRequest(PullRequestPublication publication, Workspace workspace, string commitSha, string baseBranch)
    {
        if (!string.Equals(publication.Repository, workspace.RepositoryIdentity, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(publication.HeadBranch, workspace.Branch, StringComparison.Ordinal)
            || !string.Equals(publication.HeadSha, commitSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(publication.BaseBranch, baseBranch, StringComparison.Ordinal)
            || !publication.Draft
            || !string.Equals(publication.State, "open", StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("GitHub returned an unexpected pull-request identity.");
    }

    private static PublicationException ClassifyGitNetworkFailure(string error, string message)
        => error.Contains("authentication", StringComparison.OrdinalIgnoreCase)
           || error.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
           || error.Contains("403", StringComparison.OrdinalIgnoreCase)
            ? new(message)
            : new(message, true);
}
