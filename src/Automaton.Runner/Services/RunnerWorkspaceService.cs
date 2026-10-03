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

    public string GetDeploymentStagingDirectory(Guid appId, Guid deploymentTargetId, Guid operationId)
        => GetFullPath(WorkspaceRoot, GetDeploymentStagingRelativePath(appId, deploymentTargetId, operationId));

    internal static string GetDeploymentStagingRelativePath(Guid appId, Guid deploymentTargetId, Guid operationId)
        => $"runner/deployments/{appId:D}/{deploymentTargetId:D}/{operationId:D}";

    // The workspace root is mounted at /automaton/storage/workspace inside the
    // Hosted Runner user volume. Docker volume-subpath is relative to the
    // volume root, so it needs this explicit mount-root translation.
    internal static string GetDeploymentStagingVolumeSubpath(Guid appId, Guid deploymentTargetId, Guid operationId)
        => $"workspace/{GetDeploymentStagingRelativePath(appId, deploymentTargetId, operationId)}";

    internal static string GetRuntimeCandidateVolumeSubpath(Guid appId, Guid targetId, Guid candidateId)
        => $"workspace/apps/{appId:D}/targets/{targetId:D}/artifacts/candidates/{candidateId:D}/application";

    internal static string GetApplicationDataVolumeSubpath(Guid appId, string relativePath)
    {
        if (!ManagedPathBoundary.IsSafeRelative(relativePath))
            throw new InvalidOperationException("Application data must remain inside its managed root.");
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != ".");
        return string.Join('/', new[] { $"workspace/data/{appId:D}" }.Concat(parts));
    }

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

    private string GetLegacyControllerAppRoot(Guid appId)
        => GetFullPath(GetFastApiScriptsRoot(), appId.ToString());

    private string GetLegacyControllerTargetRoot(Guid appId, Guid deploymentTargetId)
    {
        if (appId == Guid.Empty)
            throw new ArgumentException("App ID is required.", nameof(appId));
        if (deploymentTargetId == Guid.Empty)
            throw new ArgumentException(
                "Deployment target ID is required.",
                nameof(deploymentTargetId));
        return GetFullPath(
            GetFullPath(GetLegacyControllerAppRoot(appId), "targets"),
            deploymentTargetId.ToString());
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

    public string GetAppFlowArtifactsRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppRuntimeCurrentDirectory(appId, deploymentTargetId), ".automaton", "flows");

    public string ResolveAppFlowArtifactPath(Guid appId, Guid deploymentTargetId, string relativePath)
        => ManagedPathBoundary.ResolveInside(
            GetAppFlowArtifactsRoot(appId, deploymentTargetId),
            NormalizeRelativePath(relativePath),
            "managed application artifact");

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

    /// <summary>
    /// Legacy control-plane compatibility location. Generic applications must
    /// not use this path; remaining file-management consumers are Stage 3
    /// cleanup candidates.
    /// </summary>
    public string GetLegacyControllerBackendDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetLegacyControllerTargetRoot(appId, deploymentTargetId), "runtime", "backend");

    /// <summary>
    /// Legacy controller bridge virtual environment location.
    /// </summary>
    public string GetLegacyControllerVenvDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetLegacyControllerTargetRoot(appId, deploymentTargetId), "venv");

    /// <summary>
    /// Legacy controller bridge data location. In legacy scripts-layout mode
    /// this remains beside the configured scripts root because FastAPI uses it;
    /// generic runtime storage continues to use GetAppDataDirectory.
    /// </summary>
    public string GetLegacyControllerDataDirectory(Guid appId)
        => UsesLegacyScriptsLayout
            ? GetFullPath(GetLegacyControllerAppRoot(appId), "data")
            : GetAppDataDirectory(appId);

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
        PrepareWritableApplicationDirectory(dataDirectory);
        var mounts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mount in storage.Mounts)
        {
            var mountPath = ResolveApplicationDataPath(appId, mount.RelativePath);
            PrepareWritableApplicationDirectory(mountPath);
            mounts.Add(mount.Name, mountPath);
        }

        return new RunnerApplicationStorageRuntime(dataDirectory, mounts);
    }

    private void PrepareWritableApplicationDirectory(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(path);
            return;
        }

        var relativePath = Path.GetRelativePath(DataRoot, path);
        LinuxManagedDirectoryPermissions.PrepareWritableDirectory(DataRoot, relativePath);
    }

    /// <summary>
    /// Deletes deployment-target-owned state while preserving the application's
    /// persistent data root. Persistent data has a separate explicit operation.
    /// </summary>
    public void DeleteApplicationTarget(Guid appId, Guid deploymentTargetId)
    {
        var targetRoots = new[]
        {
            GetDeploymentTargetRoot(appId, deploymentTargetId),
            GetLegacyControllerTargetRoot(appId, deploymentTargetId)
        };

        foreach (var targetRoot in targetRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(targetRoot))
                Directory.Delete(targetRoot, recursive: true);
        }
    }

    public void DeleteApplicationStorage(Guid appId)
    {
        if (appId == Guid.Empty) throw new ArgumentException("App ID is required.", nameof(appId));
        var roots = new[] {
            ManagedPathBoundary.ResolveInside(AppsRoot, appId.ToString(), "app cleanup"),
            ManagedPathBoundary.ResolveInside(GetFastApiScriptsRoot(), appId.ToString(), "legacy app cleanup"),
            ManagedPathBoundary.ResolveInside(DataRoot, appId.ToString(), "app data cleanup"),
            ManagedPathBoundary.ResolveInside(RunnerRoot, $"deployments/{appId}", "app staging cleanup") };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
            GetLegacyControllerBackendDirectory(appId, deploymentTargetId),
            relativePath,
            "deployment target backend");
    }

    public string ResolveRunnerFilePath(Guid appId, Guid deploymentTargetId, string relativePath, string area)
    {
        var rootDirectory = area == AppRunnerFileAreas.Data
            ? GetLegacyControllerDataDirectory(appId)
            : GetLegacyControllerBackendDirectory(appId, deploymentTargetId);

        return ResolveRelativePath(rootDirectory, relativePath, "requested app directory");
    }

    public string ResolveAppMediaFilePath(Guid appId, string relativePath)
    {
        return ResolveRelativePath(
            GetFullPath(GetLegacyControllerDataDirectory(appId), "media"),
            relativePath,
            "app media directory");
    }

    public string ResolveAppBackendFilePath(Guid appId, Guid deploymentTargetId, string relativePath)
    {
        return ResolveRelativePath(
            GetLegacyControllerBackendDirectory(appId, deploymentTargetId),
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
