using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Serilog;

namespace Automaton.Runner.Console.Services;

public sealed class AppActionDeploymentService
{
    private const string DeploymentMetadataFileName = ".automaton-action-deployment.json";
    private readonly RunnerWorkspaceService workspace;
    private readonly AppActionExecutionCoordinator coordinator;
    private readonly ILogger logger = Log.ForContext<AppActionDeploymentService>();
    private readonly AppActionVersionStore versionStore;

    public AppActionDeploymentService(RunnerWorkspaceService workspace, AppActionExecutionCoordinator? coordinator = null)
    {
        this.workspace = workspace;
        this.coordinator = coordinator ?? new AppActionExecutionCoordinator();
        versionStore = new AppActionVersionStore(workspace);
    }

    public async Task<IReadOnlyList<AppActionDeploymentResult>> PrepareAllAsync(
        Guid appId,
        Guid deploymentTargetId,
        bool allowPrivilegedCapabilities = false,
        CancellationToken cancellationToken = default)
    {
        if (allowPrivilegedCapabilities && Automaton.Core.Enums.RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE")) != Automaton.Core.Enums.RunnerPurposes.Infrastructure)
            throw new UnauthorizedAccessException("Privileged Actions may only be prepared on Infrastructure Runners.");
        var actions = AppActionManifestParser.Discover(
            workspace.GetAppSourceRoot(appId, deploymentTargetId),
            allowPrivilegedCapabilities);

        var results = new List<AppActionDeploymentResult>(actions.Count);
        foreach (var action in actions)
        {
            results.Add(await PrepareAsync(appId, deploymentTargetId, action, cancellationToken));
        }

        return results;
    }

    public AppActionModulesResponse ListModules(Guid appId, Guid deploymentTargetId)
    {
        var actionsRoot = workspace.GetAppRuntimeActionsDirectory(appId, deploymentTargetId);
        if (!Directory.Exists(actionsRoot)) return new([]);
        var actions = new List<AppActionModuleInfo>();
        foreach (var directory in Directory.GetDirectories(actionsRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            var key = Path.GetFileName(directory);
            try
            {
                AppActionManifestParser.ValidateKey(key);
                var current = workspace.GetAppActionCurrentDirectory(appId, deploymentTargetId, key);
                var manifestPath = Path.Combine(current, "action.yaml");
                if (!File.Exists(manifestPath)) continue;
                var manifest = AppActionManifestParser.ParseFile(manifestPath, key, allowPrivilegedCapabilities: true);
                actions.Add(new(key, manifest.Metadata.Name, manifest.Metadata.Description, manifest.Spec.Runtime,
                    manifest.Spec.Entrypoint, manifest.Spec.TimeoutSeconds, AppActionVersionStore.ReadCurrentHash(current),
                    manifest.Spec.Schedulable, manifest.Spec.AllowedExecutionScopes,
                    manifest.Spec.MinimumIntervalSeconds, manifest.Spec.Input));
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Ignoring invalid deployed Action directory {ActionDirectory} for app {AppId}", directory, appId);
            }
        }
        return new(actions);
    }

    public Task<AppActionDeploymentResult> PrepareModuleAsync(PrepareAppActionModuleRequest request, CancellationToken cancellationToken)
        => AppActionModuleSourcePreparer.PrepareAsync(request, cancellationToken, PrepareAsync);

    public int SynchronizeModules(SynchronizeAppActionModulesRequest request)
    {
        var expected = request.ActionKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in expected) AppActionManifestParser.ValidateKey(key);
        var runtimeRoot = workspace.GetAppRuntimeActionsDirectory(
            request.AppId,
            request.DeploymentTargetId);
        if (!Directory.Exists(runtimeRoot)) return 0;
        var removed = 0;
        foreach (var directory in Directory.GetDirectories(runtimeRoot))
        {
            var key = Path.GetFileName(directory);
            if (expected.Contains(key)) continue;
            Directory.Delete(directory, recursive: true);
            removed++;
        }
        return removed;
    }

    public AppActionVersionsResponse ListVersions(Guid appId, Guid deploymentTargetId, string actionKey)
        => versionStore.ListVersions(appId, deploymentTargetId, actionKey);

    public AppActionVersionOperationResponse RestoreVersion(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version)
        => versionStore.RestoreVersion(appId, deploymentTargetId, actionKey, version);

    public AppActionVersionOperationResponse DeleteVersion(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version)
        => versionStore.DeleteVersion(appId, deploymentTargetId, actionKey, version);

    public Task<AppActionDeploymentResult> PrepareBundledInfrastructureAsync(
        Guid runnerHostId,
        string actionKey,
        CancellationToken cancellationToken)
    {
        TrustedInfrastructureActions.EnsureAllowed(actionKey);
        if (Automaton.Core.Enums.RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"))
            != Automaton.Core.Enums.RunnerPurposes.Infrastructure)
            throw new UnauthorizedAccessException("Privileged Actions may only be prepared on Infrastructure Runners.");
        var actionRoot = Path.Combine(AppContext.BaseDirectory, "SystemApps", "automaton-infrastructure", "actions", actionKey);
        var manifestPath = Path.Combine(actionRoot, "action.yaml");
        var manifest = AppActionManifestParser.ParseFile(manifestPath, actionKey, allowPrivilegedCapabilities: true);
        return PrepareAsync(InfrastructureAppTrust.PlatformAppId, runnerHostId,
            new DiscoveredAppAction(actionKey, actionRoot, manifestPath, manifest), cancellationToken);
    }

    private async Task<AppActionDeploymentResult> PrepareAsync(
        Guid appId,
        Guid deploymentTargetId,
        DiscoveredAppAction action,
        CancellationToken cancellationToken)
    {
        if (RunnerApplicationProfiles.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"))
            == RunnerApplicationProfiles.MachineManagement)
        {
            throw new InvalidOperationException(
                "Action dependency preparation is not available on a Machine Management Runner until it runs in an isolated container.");
        }

        using var actionLock = await coordinator.EnterAsync(
            appId,
            deploymentTargetId,
            action.Key,
            cancellationToken);
        var contentHash = AppActionSourceFiles.ComputeContentHash(action.DirectoryPath);
        var currentDirectory = workspace.GetAppActionCurrentDirectory(
            appId,
            deploymentTargetId,
            action.Key);
        if (string.Equals(AppActionVersionStore.ReadCurrentHash(currentDirectory), contentHash, StringComparison.Ordinal))
        {
            return new AppActionDeploymentResult
            {
                ActionKey = action.Key,
                Status = "unchanged",
                ContentHash = contentHash,
                Message = "Content hash matches the current deployment."
            };
        }

        var actionRoot = workspace.GetAppActionRuntimeRoot(appId, deploymentTargetId, action.Key);
        var stagingDirectory = Path.Combine(actionRoot, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(actionRoot);
        try
        {
            AppActionSourceFiles.CopyToRuntime(action.DirectoryPath, stagingDirectory);
            var entrypoint = Path.GetFullPath(Path.Combine(stagingDirectory, action.Manifest.Spec.Entrypoint));
            AppActionSourceFiles.EnsureInside(stagingDirectory, entrypoint);
            if (!File.Exists(entrypoint))
                throw new InvalidOperationException($"Action '{action.Key}' entrypoint '{action.Manifest.Spec.Entrypoint}' does not exist.");

            if (action.Manifest.Spec.Runtime == AppActionRuntimes.Python)
                await AppActionRuntimePreparer.PreparePythonAsync(stagingDirectory, cancellationToken);

            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, DeploymentMetadataFileName),
                JsonSerializer.Serialize(new { contentHash, preparedAtUtc = DateTime.UtcNow }),
                cancellationToken);

            if (Directory.Exists(currentDirectory))
            {
                var versionDirectory = Path.Combine(
                    workspace.GetAppActionVersionsDirectory(appId, deploymentTargetId, action.Key),
                    $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{AppActionVersionStore.HashSuffix(AppActionVersionStore.ReadCurrentHash(currentDirectory))}");
                Directory.CreateDirectory(Path.GetDirectoryName(versionDirectory)!);
                Directory.Move(currentDirectory, versionDirectory);
            }

            Directory.Move(stagingDirectory, currentDirectory);
            versionStore.PruneVersions(appId, deploymentTargetId, action.Key);
            return new AppActionDeploymentResult
            {
                ActionKey = action.Key,
                Status = "deployed",
                ContentHash = contentHash,
                Message = $"Prepared {action.Manifest.Spec.Runtime} Action."
            };
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to prepare Action {ActionKey} for app {AppId}", action.Key, appId);
            throw new InvalidOperationException($"Failed to prepare Action '{action.Key}': {ex.Message}", ex);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

}
