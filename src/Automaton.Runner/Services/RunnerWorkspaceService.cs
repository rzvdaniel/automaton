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

    public string AppsRoot => UseLegacyScriptsLayout
        ? Path.GetFullPath(ConfiguredRunnerScriptsDir!)
        : GetFullPath(WorkspaceRoot, "apps");

    public string DataRoot => UseLegacyScriptsLayout
        ? GetFullPath(Path.GetDirectoryName(AppsRoot) ?? WorkspaceRoot, "data")
        : GetFullPath(WorkspaceRoot, "data");

    public string CacheRoot => GetFullPath(WorkspaceRoot, "cache");

    public string RunnerRoot => GetFullPath(WorkspaceRoot, "runner");

    public string GetFastApiScriptsRoot()
    {
        return AppsRoot;
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

    public string GetAppSourceRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "source");

    public string GetAppRuntimeRoot(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "runtime");

    /// <summary>
    /// Generic application runtime artifact root. Runtime commands resolve their
    /// working directory and relative executables from here; the application
    /// contract does not know this workspace layout.
    /// </summary>
    public string GetAppRuntimeApplicationDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppRuntimeRoot(appId, deploymentTargetId), "application");

    public string GetAppRuntimeCandidateDirectory(Guid appId, Guid deploymentTargetId, Guid candidateId)
        => GetFullPath(
            GetFullPath(GetAppRuntimeRoot(appId, deploymentTargetId), "candidates"),
            candidateId.ToString());

    public string GetAppRuntimeCandidateApplicationDirectory(Guid appId, Guid deploymentTargetId, Guid candidateId)
        => GetFullPath(GetAppRuntimeCandidateDirectory(appId, deploymentTargetId, candidateId), "application");

    public string GetAppSourceFrontendDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppSourceRoot(appId, deploymentTargetId), "frontend");

    public string GetAppSourceBackendDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppSourceRoot(appId, deploymentTargetId), "backend");

    public string GetAppRuntimeFrontendDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppRuntimeRoot(appId, deploymentTargetId), "frontend");

    public string GetAppSourceActionsDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppSourceRoot(appId, deploymentTargetId), "actions");

    public string GetAppRuntimeActionsDirectory(Guid appId, Guid deploymentTargetId)
        => GetFullPath(GetAppRuntimeRoot(appId, deploymentTargetId), "actions");

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
        return GetFullPath(GetAppRuntimeRoot(appId, deploymentTargetId), "backend");
    }

    public string GetAppVenvDirectory(Guid appId, Guid deploymentTargetId)
    {
        return GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "venv");
    }

    public string GetAppVersionsDirectory(Guid appId, Guid deploymentTargetId)
    {
        return GetFullPath(GetDeploymentTargetRoot(appId, deploymentTargetId), "versions");
    }

    public string GetAppDataDirectory(Guid appId)
    {
        return UseLegacyScriptsLayout
            ? GetFullPath(GetAppRoot(appId), "data")
            : GetFullPath(DataRoot, appId.ToString());
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
        if (IsRunningInContainer())
        {
            return "/automaton/workspace";
        }

        return Path.Combine(AppContext.BaseDirectory, "workspace");
    }

    private string ResolveRelativePath(string rootDirectory, string relativePath, string scopeDescription)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var fullRoot = Path.GetFullPath(rootDirectory);

        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path must stay inside the {scopeDescription}.");
        }

        return fullPath;
    }

    private static string GetFullPath(string root, string child)
    {
        return Path.GetFullPath(Path.Combine(root, child));
    }

    private static bool IsRunningInContainer()
    {
        return string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
            || File.Exists("/.dockerenv");
    }
}
