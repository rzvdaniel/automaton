using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerDeploymentWorkspace(RunnerWorkspaceService workspace)
{
    private const long MaximumExtractedBytes = 250L * 1024 * 1024;
    private const int MaximumArchiveEntries = 20_000;

    public static AppDeploymentPackageManifest LoadManifest(string extractRoot, Guid requestAppId)
    {
        var manifestPath = Path.Combine(extractRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("Deployment package is missing manifest.json.");
        var manifest = JsonSerializer.Deserialize<AppDeploymentPackageManifest>(File.ReadAllText(manifestPath));
        if (manifest is null || manifest.Version != 1 || string.IsNullOrWhiteSpace(manifest.AppId))
            throw new InvalidOperationException("Deployment package manifest is invalid.");
        if (!Guid.TryParse(manifest.AppId, out var manifestAppId) || manifestAppId != requestAppId)
            throw new InvalidOperationException("Deployment package manifest app ID does not match the deployment request.");
        ApplicationContractValidator.EnsureDeployable(manifest.Application);
        return manifest;
    }

    public string? CreateSnapshotIfNeeded(Guid appId, Guid deploymentTargetId)
    {
        var targetRoot = workspace.GetDeploymentTargetRoot(appId, deploymentTargetId);
        if (!Directory.Exists(targetRoot)) return null;
        var roots = new[]
        {
            workspace.GetAppSourceRoot(appId, deploymentTargetId),
            workspace.GetAppArtifactsRoot(appId, deploymentTargetId)
        };
        if (!roots.Any(Directory.Exists)) return null;
        var snapshotRoot = Path.Combine(
            workspace.GetAppVersionsDirectory(appId, deploymentTargetId),
            DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        foreach (var root in roots.Where(Directory.Exists))
        {
            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            CopyDirectory(root, Path.Combine(snapshotRoot, name));
        }
        return snapshotRoot;
    }

    public void CleanDeploymentRoots(Guid appId, Guid deploymentTargetId, bool includeRuntime = true)
    {
        DeleteDirectoryIfExists(workspace.GetAppSourceRoot(appId, deploymentTargetId));
        if (includeRuntime) DeleteDirectoryIfExists(workspace.GetAppArtifactsRoot(appId, deploymentTargetId));
        DeleteDirectoryIfExists(workspace.GetAppCacheRoot(appId, deploymentTargetId));
    }

    public void RestoreSnapshot(Guid appId, Guid deploymentTargetId, string snapshotPath, bool restoreRuntime = true)
    {
        if (!Directory.Exists(snapshotPath))
            throw new DirectoryNotFoundException("The previous App snapshot is unavailable.");
        DeleteDirectoryIfExists(workspace.GetAppSourceRoot(appId, deploymentTargetId));
        if (restoreRuntime) DeleteDirectoryIfExists(workspace.GetAppArtifactsRoot(appId, deploymentTargetId));
        var source = Path.Combine(snapshotPath, "source");
        var runtime = Path.Combine(snapshotPath, "artifacts");
        if (Directory.Exists(source)) CopyDirectory(source, workspace.GetAppSourceRoot(appId, deploymentTargetId));
        if (restoreRuntime && Directory.Exists(runtime))
            CopyDirectory(runtime, workspace.GetAppArtifactsRoot(appId, deploymentTargetId));
    }

    public static void ValidateIncomingActions(string extractRoot, bool allowPrivilegedCapabilities)
    {
        var sourceRoot = Path.Combine(extractRoot, "source");
        foreach (var action in AppActionManifestParser.Discover(sourceRoot, allowPrivilegedCapabilities))
        {
            var entrypoint = Path.GetFullPath(Path.Combine(action.DirectoryPath, action.Manifest.Spec.Entrypoint));
            var actionRoot = Path.GetFullPath(action.DirectoryPath);
            if (!entrypoint.StartsWith(actionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(entrypoint))
                throw new InvalidOperationException($"Action '{action.Key}' entrypoint does not exist inside its Action folder.");
        }
    }

    public void SynchronizeActionRoots(Guid appId, Guid deploymentTargetId, string extractRoot)
    {
        var incomingRoot = Path.Combine(extractRoot, "source", "actions");
        // Deployment packages are complete source snapshots. A missing actions/
        // directory therefore means that the App now has zero Actions (ZIP files
        // cannot reliably represent empty directories).
        var incomingKeys = Directory.Exists(incomingRoot)
            ? Directory.GetDirectories(incomingRoot).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string?>(StringComparer.Ordinal);
        DeleteDirectoryIfExists(workspace.GetAppSourceActionsDirectory(appId, deploymentTargetId));
        var runtimeRoot = workspace.GetAppRuntimeActionsDirectory(appId, deploymentTargetId);
        if (!Directory.Exists(runtimeRoot)) return;
        foreach (var directory in Directory.GetDirectories(runtimeRoot)
            .Where(directory => !incomingKeys.Contains(Path.GetFileName(directory))))
            Directory.Delete(directory, recursive: true);
    }

    public static int CopyTreeIfExists(string sourceRoot, string destinationRoot)
        => Directory.Exists(sourceRoot) ? CopyDirectory(sourceRoot, destinationRoot) : 0;

    public static int CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
        foreach (var sourceFile in files)
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFile = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
        return files.Length;
    }

    public static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    public static void ExtractPackageSafely(string packagePath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new InvalidOperationException($"Deployment package exceeds the {MaximumArchiveEntries} entry limit.");
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType is 0xA000 or 0x6000 or 0x2000)
                throw new InvalidOperationException($"Deployment package contains an unsupported link or device entry: {entry.FullName}");
            AppActionManifestParser.ValidateRelativePath(entry.FullName, "package entry");
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumExtractedBytes)
                throw new InvalidOperationException("Deployment package exceeds the 250 MB extracted-size limit.");
            var target = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            var fullRoot = Path.GetFullPath(destinationRoot);
            if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Deployment package entry escapes the staging directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
