using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public class RunnerWorkspaceService
{
    private const string WorkspaceRootEnvironmentVariable = "AUTOMATON_WORKSPACE_ROOT";
    private const string RunnerScriptsDirEnvironmentVariable = "RUNNER_SCRIPTS_DIR";

    private string? ConfiguredWorkspaceRoot =>
        Environment.GetEnvironmentVariable(WorkspaceRootEnvironmentVariable);

    private string? ConfiguredRunnerScriptsDir =>
        Environment.GetEnvironmentVariable(RunnerScriptsDirEnvironmentVariable);

    private bool UseLegacyScriptsLayout =>
        string.IsNullOrWhiteSpace(ConfiguredWorkspaceRoot)
        && !string.IsNullOrWhiteSpace(ConfiguredRunnerScriptsDir);

    public bool UsesLegacyScriptsLayout => UseLegacyScriptsLayout;

    public string WorkspaceRoot => Path.GetFullPath(
        ConfiguredWorkspaceRoot
        ?? GetDefaultWorkspaceRoot());

    // Generic runner storage always uses the configured workspace boundary.
    // RUNNER_SCRIPTS_DIR is retained only by the deferred controller bridge.
    public string AppsRoot => GetFullPath(WorkspaceRoot, "apps");

    public string DataRoot => GetFullPath(WorkspaceRoot, "data");

    public string CacheRoot => GetFullPath(WorkspaceRoot, "cache");

    public string RunnerRoot => GetFullPath(WorkspaceRoot, "runner");

    public string GetFastApiScriptsRoot()
    {
        return UseLegacyScriptsLayout
            ? Path.GetFullPath(ConfiguredRunnerScriptsDir!)
            : AppsRoot;
    }

    public string GetAppRoot(Guid appId)
    {
        return GetFullPath(AppsRoot, appId.ToString());
    }

    public string GetDeploymentTargetRoot(Guid appId, Guid deploymentTargetId)
    {
        if (appId == Guid.Empty)
            throw new ArgumentException("App ID is required.", nameof(appId));
        if (deploymentTargetId == Guid.Empty)
            throw new ArgumentException(
                "Deployment target ID is required.",
                nameof(deploymentTargetId));
        return GetFullPath(
            GetFullPath(GetAppRoot(appId), "targets"),
            deploymentTargetId.ToString());
    }

    public string GetAppWorkspaceRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "workspace");

    public string GetAppAutomationRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "automaton");

    public string GetAppArtifactsRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "artifacts");

    public string GetAppLogsRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "logs");

    public string GetAppCacheRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "cache");

    public string GetAppRuntimeLogsDirectory(
        Guid appId,
        Guid deploymentTargetId,
        string runtimeIdentity)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentity)
            || runtimeIdentity.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Runtime identity is invalid.", nameof(runtimeIdentity));
        return GetFullPath(GetAppLogsRoot(appId, deploymentTargetId), runtimeIdentity);
    }

    public RunnerApplicationStoragePaths GetApplicationStoragePaths(Guid appId, Guid deploymentTargetId)
        => new(
            GetAppRoot(appId),
            GetAppWorkspaceRoot(appId, deploymentTargetId),
            GetAppSourceRoot(appId, deploymentTargetId),
            GetAppAutomationRoot(appId, deploymentTargetId),
            GetAppArtifactsRoot(appId, deploymentTargetId),
            GetAppRuntimeRoot(appId, deploymentTargetId),
            GetAppLogsRoot(appId, deploymentTargetId),
            GetAppCacheRoot(appId, deploymentTargetId),
            GetAppDataDirectory(appId));

    public string GetAppSourceRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppWorkspaceRoot(appId, deploymentTargetId), "source");

    public string GetAppRuntimeRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "runtime");

    /// <summary>
    /// Generic application runtime artifact root. Runtime commands resolve their
    /// working directory and relative executables from here; the application
    /// contract does not know this workspace layout.
    /// </summary>
    public string GetAppRuntimeApplicationDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppArtifactsRoot(appId, deploymentTargetId), "current", "application");

    public string GetAppRuntimeCurrentDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppArtifactsRoot(appId, deploymentTargetId), "current");

    public string GetAppRuntimeCandidateDirectory(Guid appId, Guid deploymentTargetId, Guid candidateId)
        => GetFullPath(
            GetFullPath(GetAppArtifactsRoot(appId, deploymentTargetId), "candidates"),
            candidateId.ToString());

    public string GetAppRuntimeCandidateApplicationDirectory(Guid appId, Guid deploymentTargetId, Guid candidateId)
        => GetFullPath(GetAppRuntimeCandidateDirectory(appId, deploymentTargetId, candidateId), "application");

    public string GetAppSourceFrontendDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppSourceRoot(appId, deploymentTargetId), "frontend");

    public string GetAppSourceBackendDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppSourceRoot(appId, deploymentTargetId), "backend");

    public string GetAppRuntimeFrontendDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppArtifactsRoot(appId, deploymentTargetId), "current", "frontend");

    public string GetAppSourceActionsDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppSourceRoot(appId, deploymentTargetId), "actions");

    public string GetAppRuntimeActionsDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppArtifactsRoot(appId, deploymentTargetId), "current", "actions");

    public string GetAppActionRuntimeRoot(Guid appId, Guid deploymentTargetId, string actionKey)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        return GetFullPath(GetAppRuntimeActionsDirectory(appId, deploymentTargetId), actionKey);
    }

    public string GetAppActionCurrentDirectory(Guid appId, Guid deploymentTargetId, string actionKey)
        => GetFullPath(GetAppActionRuntimeRoot(appId, deploymentTargetId, actionKey), "current");

    public string GetAppActionVersionsDirectory(Guid appId, Guid deploymentTargetId, string actionKey)
        => GetFullPath(GetAppActionRuntimeRoot(appId, deploymentTargetId, actionKey), "versions");

    public string GetAppActionExecutionsDirectory(Guid appId, Guid deploymentTargetId, string actionKey)
        => GetFullPath(GetAppActionRuntimeRoot(appId, deploymentTargetId, actionKey), "executions");

    public string GetAppBackendDirectory(Guid appId, Guid deploymentTargetId)
    {
        return GetFullPath(GetAppSourceRoot(appId, deploymentTargetId), "backend");
    }

    public string GetAppVenvDirectory(Guid appId, Guid deploymentTargetId)
    {
        return GetFullPath(GetAppCacheRoot(appId, deploymentTargetId), "legacy-python", "venv");
    }

    public string GetAppVersionsDirectory(Guid appId, Guid deploymentTargetId)
    {
        return GetFullPath(GetAppAutomationRoot(appId, deploymentTargetId), "snapshots");
    }

    public string GetAppDataDirectory(Guid appId)
    {
        return GetFullPath(DataRoot, appId.ToString());
    }

    public string ResolveApplicationDataPath(Guid appId, string relativePath)
        => ManagedPathBoundary.ResolveInside(
            GetAppDataDirectory(appId),
            relativePath,
            "application persistent-data path");

    public RunnerApplicationStorageRuntime PrepareApplicationStorage(
        Guid appId,
        ApplicationStorageSpec storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ApplicationContractValidator.EnsureValid(new ApplicationContract { Storage = storage });

        var dataDirectory = ResolveApplicationDataPath(appId, storage.DataDirectory);
        Directory.CreateDirectory(dataDirectory);
        var mounts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mount in storage.Mounts)
        {
            var mountPath = ResolveApplicationDataPath(appId, mount.RelativePath);
            Directory.CreateDirectory(mountPath);
            mounts.Add(mount.Name, mountPath);
        }

        return new RunnerApplicationStorageRuntime(dataDirectory, mounts);
    }

    /// <summary>
    /// Deletes deployment-target-owned state while preserving the application's
    /// persistent data root. Persistent data has a separate explicit operation.
    /// </summary>
    public void DeleteApplicationTarget(Guid appId, Guid deploymentTargetId)
    {
        var targetRoot = GetDeploymentTargetRoot(appId, deploymentTargetId);
        if (Directory.Exists(targetRoot))
            Directory.Delete(targetRoot, recursive: true);
    }

    public void DeletePersistentApplicationData(Guid appId)
    {
        var dataRoot = GetAppDataDirectory(appId);
        if (Directory.Exists(dataRoot))
            Directory.Delete(dataRoot, recursive: true);
    }

    public string ResolveStorageMountPath(Guid appId, ApplicationStorageMount mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ApplicationContractValidator.EnsureValid(new ApplicationContract
        {
            Storage = new ApplicationStorageSpec { Mounts = [mount] }
        });
        return ResolveApplicationDataPath(appId, mount.RelativePath);
    }

    public string ResolveBackendUploadPath(Guid appId, Guid deploymentTargetId, string relativePath)
    {
        return ResolveRelativePath(
            GetAppBackendDirectory(appId, deploymentTargetId),
            relativePath,
            "deployment target backend");
    }

    public string ResolveRunnerFilePath(Guid appId, Guid deploymentTargetId, string relativePath, string area)
    {
        var rootDirectory = area == AppRunnerFileAreas.Data
            ? GetAppDataDirectory(appId)
            : GetAppBackendDirectory(appId, deploymentTargetId);

        return ResolveRelativePath(rootDirectory, relativePath, "requested app directory");
    }

    public string ResolveAppMediaFilePath(Guid appId, string relativePath)
    {
        return ResolveRelativePath(GetFullPath(GetAppDataDirectory(appId), "media"), relativePath, "app media directory");
    }

    public string ResolveAppBackendFilePath(Guid appId, Guid deploymentTargetId, string relativePath)
    {
        return ResolveRelativePath(
            GetAppBackendDirectory(appId, deploymentTargetId),
            relativePath,
            "deployment target backend directory");
    }

    public string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || Path.IsPathRooted(relativePath)
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Invalid backend file path.");
        }

        return normalized;
    }

    private static string GetDefaultWorkspaceRoot()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RunnerScriptsDirEnvironmentVariable)))
        {
            var scriptsRoot = Path.GetFullPath(Environment.GetEnvironmentVariable(RunnerScriptsDirEnvironmentVariable)!);
            return Path.GetFullPath(Path.Combine(scriptsRoot, ".."));
        }

        if (IsRunningInContainer())
        {
            return "/automaton/workspace";
        }

        return Path.Combine(AppContext.BaseDirectory, "workspace");
    }

    /// <summary>
    /// Moves the pre-Stage 4 target layout into the explicit workspace,
    /// artifact, log, cache, and automation roots. It is intentionally a
    /// one-way migration: persistent source/data are retained, while old
    /// directory names are not used as generic fallbacks afterward.
    /// </summary>
    public void MigrateLegacyLayout()
    {
        if (!Directory.Exists(AppsRoot))
            return;

        foreach (var appRoot in Directory.GetDirectories(AppsRoot))
        {
            if (!Guid.TryParse(Path.GetFileName(appRoot), out var appId))
                continue;

            var oldData = Path.Combine(appRoot, "data");
            if (Directory.Exists(oldData))
                MoveTree(oldData, GetAppDataDirectory(appId));

            var targetsRoot = Path.Combine(appRoot, "targets");
            if (!Directory.Exists(targetsRoot))
                continue;

            foreach (var targetRoot in Directory.GetDirectories(targetsRoot))
            {
                if (Guid.TryParse(Path.GetFileName(targetRoot), out var targetId))
                    MigrateLegacyTarget(targetRoot, appId, targetId);
            }
        }
    }

    private void MigrateLegacyTarget(string oldTargetRoot, Guid appId, Guid deploymentTargetId)
    {
        var oldSource = Path.Combine(oldTargetRoot, "source");
        if (Directory.Exists(oldSource))
            MoveTree(oldSource, GetAppSourceRoot(appId, deploymentTargetId));

        var oldRuntime = Path.Combine(oldTargetRoot, "runtime");
        if (Directory.Exists(oldRuntime))
        {
            var current = GetFullPath(GetAppArtifactsRoot(appId, deploymentTargetId), "current");
            var oldApplication = Path.Combine(oldRuntime, "application");
            if (Directory.Exists(oldApplication))
                MoveTree(oldApplication, GetAppRuntimeApplicationDirectory(appId, deploymentTargetId));

            var oldCandidates = Path.Combine(oldRuntime, "candidates");
            if (Directory.Exists(oldCandidates))
                MoveTree(oldCandidates, GetFullPath(GetAppArtifactsRoot(appId, deploymentTargetId), "candidates"));

            foreach (var entry in Directory.GetFileSystemEntries(oldRuntime))
            {
                if (string.Equals(Path.GetFileName(entry), "application", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(entry), "candidates", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(Path.GetFileName(entry), "logs", StringComparison.OrdinalIgnoreCase))
                    MoveTree(
                        entry,
                        Directory.Exists(entry)
                            ? GetAppLogsRoot(appId, deploymentTargetId)
                            : Path.Combine(GetAppLogsRoot(appId, deploymentTargetId), Path.GetFileName(entry)));
                else
                    MoveTree(
                        entry,
                        Directory.Exists(entry)
                            ? current
                            : Path.Combine(current, Path.GetFileName(entry)));
            }

            DeleteEmptyDirectory(oldRuntime);
        }

        MigrateMetadataFile(
            Path.Combine(oldTargetRoot, "deployment.json"),
            Path.Combine(GetAppAutomationRoot(appId, deploymentTargetId), "deployment.json"),
            oldRuntime,
            GetAppArtifactsRoot(appId, deploymentTargetId));
        MoveTree(
            Path.Combine(oldTargetRoot, "venv"),
            GetAppVenvDirectory(appId, deploymentTargetId));
        MoveTree(
            Path.Combine(oldTargetRoot, "versions"),
            GetAppVersionsDirectory(appId, deploymentTargetId));
    }

    private static void MoveTree(string source, string destination)
    {
        if (!Directory.Exists(source) && !File.Exists(source))
            return;
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
                File.Move(source, destination);
            return;
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
                File.Move(file, target);
        }

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
            DeleteEmptyDirectory(directory);
        DeleteEmptyDirectory(source);
    }

    private static void MigrateMetadataFile(
        string source,
        string destination,
        string oldRuntimeRoot,
        string newArtifactsRoot)
    {
        if (!File.Exists(source))
            return;

        var metadata = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(
            File.ReadAllText(source),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (metadata is null)
            throw new InvalidOperationException($"Runtime metadata at '{source}' is invalid.");

        metadata.ArtifactRoot = MapLegacyArtifactPath(metadata.ArtifactRoot, oldRuntimeRoot, newArtifactsRoot);
        if (metadata.Candidate is not null)
        {
            metadata.Candidate.ArtifactRoot = MapLegacyArtifactPath(
                metadata.Candidate.ArtifactRoot,
                oldRuntimeRoot,
                newArtifactsRoot);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        File.Delete(source);
    }

    private static string? MapLegacyArtifactPath(
        string? path,
        string oldRuntimeRoot,
        string newArtifactsRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var oldRoot = Path.GetFullPath(oldRuntimeRoot);
        var fullPath = Path.GetFullPath(path);
        if (!ManagedPathBoundary.IsWithin(oldRoot, fullPath))
            return path;

        var relative = Path.GetRelativePath(oldRoot, fullPath);
        if (relative.Equals("application", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(newArtifactsRoot, "current", "application");
        if (relative.StartsWith("candidates" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(newArtifactsRoot, relative);
        return Path.Combine(newArtifactsRoot, "current", relative);
    }

    private static void DeleteEmptyDirectory(string path)
    {
        if (Directory.Exists(path)
            && !Directory.EnumerateFileSystemEntries(path).Any())
            Directory.Delete(path);
    }

    private string ResolveRelativePath(string rootDirectory, string relativePath, string scopeDescription)
    {
        var normalized = NormalizeRelativePath(relativePath);
        try
        {
            return ManagedPathBoundary.ResolveInside(rootDirectory, normalized, scopeDescription);
        }
        catch (InvalidOperationException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
    }

    private static string GetFullPath(string root, params string[] children)
    {
        var path = root;
        foreach (var child in children)
            path = Path.Combine(path, child);
        return Path.GetFullPath(path);
    }

    private static bool IsRunningInContainer()
    {
        return string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
            || File.Exists("/.dockerenv");
    }
}
