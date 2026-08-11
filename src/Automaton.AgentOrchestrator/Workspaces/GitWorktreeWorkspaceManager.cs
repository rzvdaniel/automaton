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

        var created = new WorkspaceSidecar(
            key, request.WorkItem.Id, request.Repository.Identity, request.Repository.RootPath, workspacePath, branch,
            WorkspaceLifecycleStates.Preparing, WorkspaceExecutionOwnershipStates.None, Guid.NewGuid().ToString("N"), clock.UtcNow, clock.UtcNow);
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
            UpdatedAtUtc = clock.UtcNow
        };
        await WriteSidecarAsync(sidecarPath, updated, cancellationToken);
        return ToWorkspace(updated);
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
            : new[] { "worktree", "add", "-b", sidecar.Branch, sidecar.WorkspacePath, "HEAD" };
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

    private async Task WriteSidecarAsync(string path, WorkspaceSidecar sidecar, CancellationToken cancellationToken)
    {
        EnsureContained(path, SidecarRoot());
        Directory.CreateDirectory(SidecarRoot());
        EnsureNoReparsePoints(SidecarRoot());
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, sidecar, json, cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    private void ValidateRepository(WorkspaceRepository repository)
    {
        if (string.IsNullOrWhiteSpace(repository.Identity)) throw new WorkspaceException("Workspace repository identity is required.");
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
            || !PathsEqual(sidecar.RepositoryRootPath, request.Repository.RootPath)
            || !string.Equals(sidecar.WorkspaceKey, key, StringComparison.Ordinal)
            || !PathsEqual(sidecar.WorkspacePath, workspacePath)
            || !string.Equals(sidecar.Branch, branch, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(sidecar.LaunchNonce)
            || !IsKnownLifecycleStatus(sidecar.LifecycleStatus)
            || !IsKnownOwnershipStatus(sidecar.ExecutionOwnershipStatus))
            throw new WorkspaceRecoveryException("Workspace sidecar state does not match the requested daemon-owned workspace.");
        EnsureContained(sidecar.WorkspacePath, workspaceRoot);
    }

    private void ValidateWorkspaceMatchesSidecar(Workspace workspace, WorkspaceSidecar sidecar)
    {
        if (!string.Equals(workspace.Key, sidecar.WorkspaceKey, StringComparison.Ordinal)
            || !PathsEqual(workspace.Path, sidecar.WorkspacePath)
            || !string.Equals(workspace.Branch, sidecar.Branch, StringComparison.Ordinal)
            || !string.Equals(workspace.ExecutionOwnershipStatus, sidecar.ExecutionOwnershipStatus, StringComparison.Ordinal)
            || !string.Equals(workspace.RepositoryIdentity, sidecar.RepositoryIdentity, StringComparison.Ordinal)
            || !string.Equals(workspace.LaunchNonce, sidecar.LaunchNonce, StringComparison.Ordinal))
            throw new WorkspaceRecoveryException("Workspace does not match daemon-owned sidecar state.");
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
        => new(sidecar.WorkspaceKey, sidecar.WorkspacePath, sidecar.Branch, sidecar.LifecycleStatus, sidecar.ExecutionOwnershipStatus, sidecar.LaunchNonce, sidecar.RepositoryIdentity);

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
        DateTimeOffset UpdatedAtUtc);
}

public class WorkspaceException(string message) : Exception(message);
public sealed class WorkspaceRecoveryException(string message) : WorkspaceException(message);
public sealed class WorkspaceOwnershipException(string message) : WorkspaceException(message);
