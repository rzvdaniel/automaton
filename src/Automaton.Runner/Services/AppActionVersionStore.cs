using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal sealed class AppActionVersionStore(RunnerWorkspaceService workspace)
{
    private const string DeploymentMetadataFileName = ".automaton-action-deployment.json";
    private const int DefaultMaximumVersionsPerAction = 10;
    private const long DefaultMaximumVersionBytesPerAction = 1L * 1024 * 1024 * 1024;

    public AppActionVersionsResponse ListVersions(Guid appId, Guid deploymentTargetId, string actionKey)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        var versions = new List<AppActionVersionInfo>();
        var current = workspace.GetAppActionCurrentDirectory(appId, deploymentTargetId, actionKey);
        if (Directory.Exists(current)) versions.Add(ToVersionInfo(current, "current", isCurrent: true));
        var versionsRoot = workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey);
        if (Directory.Exists(versionsRoot))
            versions.AddRange(Directory.GetDirectories(versionsRoot)
                .Select(path => ToVersionInfo(path, Path.GetFileName(path), isCurrent: false))
                .OrderByDescending(version => version.CreatedAtUtc));
        return new(versions);
    }

    public AppActionVersionOperationResponse RestoreVersion(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version)
    {
        ValidateVersionName(version);
        var versionsRoot = workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey);
        var selected = EnsureChildPath(versionsRoot, version);
        if (!Directory.Exists(selected)) return new(false, "Action version was not found.");
        var current = workspace.GetAppActionCurrentDirectory(appId, deploymentTargetId, actionKey);
        var staging = Path.Combine(workspace.GetAppActionRuntimeRoot(appId, deploymentTargetId, actionKey), $".restore-{Guid.NewGuid():N}");
        CopyDirectory(selected, staging);
        ValidatePreparedDeployment(staging, actionKey);
        if (Directory.Exists(current))
        {
            var backup = Path.Combine(versionsRoot, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{HashSuffix(ReadCurrentHash(current))}");
            Directory.CreateDirectory(versionsRoot);
            Directory.Move(current, backup);
        }
        Directory.Move(staging, current);
        return new(true, $"Restored Action '{actionKey}' to version '{version}'.", ReadCurrentHash(current));
    }

    public AppActionVersionOperationResponse DeleteVersion(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version)
    {
        ValidateVersionName(version);
        var path = EnsureChildPath(workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey), version);
        if (!Directory.Exists(path)) return new(false, "Action version was not found.");
        Directory.Delete(path, recursive: true);
        return new(true, $"Deleted Action version '{version}'.");
    }

    public async Task PublishAsync(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string stagingDirectory,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var current = workspace.GetAppActionCurrentDirectory(appId, deploymentTargetId, actionKey);
        await AppActionDeploymentOutputGuard.WriteMetadataAsync(
            stagingDirectory,
            DeploymentMetadataFileName,
            JsonSerializer.Serialize(new { contentHash, preparedAtUtc = DateTime.UtcNow }),
            cancellationToken);
        AppActionDeploymentOutputGuard.EnsureNoLinks(stagingDirectory);

        if (Directory.Exists(current))
        {
            var version = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{HashSuffix(ReadCurrentHash(current))}";
            var versionDirectory = Path.Combine(workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey), version);
            Directory.CreateDirectory(Path.GetDirectoryName(versionDirectory)!);
            Directory.Move(current, versionDirectory);
        }

        Directory.Move(stagingDirectory, current);
        PruneVersions(appId, deploymentTargetId, actionKey);
    }

    public void PruneVersions(Guid appId, Guid deploymentTargetId, string actionKey)
    {
        var root = workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, actionKey);
        if (!Directory.Exists(root)) return;
        var versions = Directory.GetDirectories(root)
            .Select(path => new VersionEntry(path, Directory.GetCreationTimeUtc(path), GetDirectorySize(path)))
            .OrderByDescending(item => item.Created)
            .ToList();
        PruneToLimits(versions,
            ReadPositiveIntEnvironment("AUTOMATON_ACTION_MAX_VERSIONS", DefaultMaximumVersionsPerAction, 1, 1000),
            ReadPositiveLongEnvironment("AUTOMATON_ACTION_MAX_VERSION_BYTES", DefaultMaximumVersionBytesPerAction, 1L * 1024 * 1024, 100L * 1024 * 1024 * 1024));
    }

    public static string? ReadCurrentHash(string currentDirectory)
    {
        var path = Path.Combine(currentDirectory, DeploymentMetadataFileName);
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("contentHash", out var value) ? value.GetString() : null;
    }

    public static string HashSuffix(string? hash)
        => !string.IsNullOrWhiteSpace(hash) && hash.Length >= 20 ? hash[12..20] : "unknown";

    private static long GetDirectorySize(string path)
        => Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    private static void PruneToLimits(IReadOnlyList<VersionEntry> versions, int maximumVersions, long maximumBytes)
    {
        var retainedBytes = 0L;
        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];
            if (index < maximumVersions && retainedBytes + version.Size <= maximumBytes)
            {
                retainedBytes += version.Size;
                continue;
            }
            Directory.Delete(version.Path, recursive: true);
        }
    }

    private static AppActionVersionInfo ToVersionInfo(string path, string version, bool isCurrent)
    {
        var size = GetDirectorySize(path);
        return new(version, ReadCurrentHashFromPath(path), Directory.GetCreationTimeUtc(path), size, isCurrent);
    }

    private static string? ReadCurrentHashFromPath(string directory)
    {
        var path = Path.Combine(directory, DeploymentMetadataFileName);
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("contentHash", out var value) ? value.GetString() : null;
    }

    private static void ValidatePreparedDeployment(string directory, string actionKey)
    {
        var manifestPath = Path.Combine(directory, "action.yaml");
        if (!File.Exists(manifestPath)) throw new InvalidOperationException("Action version is missing action.yaml.");
        var manifest = AppActionManifestParser.ParseFile(manifestPath, actionKey, allowPrivilegedCapabilities: true);
        var entrypoint = Path.GetFullPath(Path.Combine(directory, manifest.Spec.Entrypoint));
        EnsureInside(directory, entrypoint);
        if (!File.Exists(entrypoint)) throw new InvalidOperationException("Action version entrypoint is missing.");
        if (manifest.Spec.Runtime == AppActionRuntimes.Python && !PythonEnvironmentExists(directory))
            throw new InvalidOperationException("Python Action version is missing its prepared virtual environment.");
    }

    private static bool PythonEnvironmentExists(string directory)
    {
        var python = OperatingSystem.IsWindows()
            ? Path.Combine(directory, ".venv", "Scripts", "python.exe")
            : Path.Combine(directory, ".venv", "bin", "python");
        return File.Exists(python);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, sourceFile));
            EnsureInside(destination, target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourceFile, target, overwrite: true);
        }
    }

    private static void ValidateVersionName(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version is "current" or "." or ".." || version.Length > 100
            || version.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidOperationException("Invalid Action version identifier.");
    }

    private static string EnsureChildPath(string root, string child)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(root, child));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Action version path must remain inside the versions directory.");
        return fullPath;
    }

    private static void EnsureInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Action files must stay inside the Action folder.");
    }

    private static int ReadPositiveIntEnvironment(string name, int fallback, int minimum, int maximum)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static long ReadPositiveLongEnvironment(string name, long fallback, long minimum, long maximum)
        => long.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private sealed record VersionEntry(string Path, DateTime Created, long Size);
}
