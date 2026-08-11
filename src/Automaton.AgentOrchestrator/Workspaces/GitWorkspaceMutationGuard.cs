using System.Security.Cryptography;
using System.Text;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;

namespace Automaton.AgentOrchestrator.Workspaces;

public sealed class GitWorkspaceMutationGuard(IGitCommandRunner git) : IWorkspaceMutationGuard
{
    public async Task<string> CaptureAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(workspace.Path);
        if (!Directory.Exists(root)) throw new WorkspaceRecoveryException("Assigned workspace does not exist.");
        var diff = await git.RunAsync(root, ["diff", "--binary", "HEAD"], cancellationToken);
        var untracked = await git.RunAsync(root, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken);
        if (diff.ExitCode != 0 || untracked.ExitCode != 0) throw new WorkspaceRecoveryException("Git could not capture the workspace mutation fingerprint.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(diff.StandardOutput));
        foreach (var relative in untracked.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsContained(path, root) || !File.Exists(path)) throw new WorkspaceRecoveryException("Untracked workspace path is unsafe or changed during inspection.");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new WorkspaceRecoveryException("Untracked workspace path cannot be inspected through a link or reparse point.");
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            await using var stream = File.OpenRead(path);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0) hash.AppendData(buffer.AsSpan(0, read));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool IsContained(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(path, normalizedRoot, comparison) || path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
