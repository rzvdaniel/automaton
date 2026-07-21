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

    public string GetAppSourceRoot(Guid appId) => GetFullPath(GetAppRoot(appId), "source");

    public string GetAppRuntimeRoot(Guid appId) => GetFullPath(GetAppRoot(appId), "runtime");

    public string GetAppSourceFrontendDirectory(Guid appId) => GetFullPath(GetAppSourceRoot(appId), "frontend");

    public string GetAppSourceBackendDirectory(Guid appId) => GetFullPath(GetAppSourceRoot(appId), "backend");

    public string GetAppRuntimeFrontendDirectory(Guid appId) => GetFullPath(GetAppRuntimeRoot(appId), "frontend");

    public string GetAppSourceActionsDirectory(Guid appId) => GetFullPath(GetAppSourceRoot(appId), "actions");

    public string GetAppRuntimeActionsDirectory(Guid appId) => GetFullPath(GetAppRuntimeRoot(appId), "actions");

    public string GetAppActionRuntimeRoot(Guid appId, string actionKey)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        return GetFullPath(GetAppRuntimeActionsDirectory(appId), actionKey);
    }

    public string GetAppActionCurrentDirectory(Guid appId, string actionKey)
        => GetFullPath(GetAppActionRuntimeRoot(appId, actionKey), "current");

    public string GetAppActionVersionsDirectory(Guid appId, string actionKey)
        => GetFullPath(GetAppActionRuntimeRoot(appId, actionKey), "versions");

    public string GetAppActionExecutionsDirectory(Guid appId, string actionKey)
        => GetFullPath(GetAppActionRuntimeRoot(appId, actionKey), "executions");

    public string GetAppBackendDirectory(Guid appId)
    {
        return GetFullPath(GetAppRuntimeRoot(appId), "backend");
    }

    public string GetAppVenvDirectory(Guid appId)
    {
        return GetFullPath(GetAppRoot(appId), "venv");
    }

    public string GetAppVersionsDirectory(Guid appId)
    {
        return GetFullPath(GetAppRoot(appId), "versions");
    }

    public string GetAppDataDirectory(Guid appId)
    {
        return UseLegacyScriptsLayout
            ? GetFullPath(GetAppRoot(appId), "data")
            : GetFullPath(DataRoot, appId.ToString());
    }

    public string ResolveBackendUploadPath(Guid appId, string relativePath)
    {
        return ResolveRelativePath(GetAppBackendDirectory(appId), relativePath, "app backend");
    }

    public string ResolveRunnerFilePath(Guid appId, string relativePath, string area)
    {
        var rootDirectory = area == AppRunnerFileAreas.Data
            ? GetAppDataDirectory(appId)
            : GetAppBackendDirectory(appId);

        return ResolveRelativePath(rootDirectory, relativePath, "requested app directory");
    }

    public string ResolveAppMediaFilePath(Guid appId, string relativePath)
    {
        return ResolveRelativePath(GetFullPath(GetAppDataDirectory(appId), "media"), relativePath, "app media directory");
    }

    public string ResolveAppBackendFilePath(Guid appId, string relativePath)
    {
        return ResolveRelativePath(GetAppBackendDirectory(appId), relativePath, "app backend directory");
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
