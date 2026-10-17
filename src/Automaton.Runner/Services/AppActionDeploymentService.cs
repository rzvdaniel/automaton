using Automaton.Core.Enums;
using Automaton.Core.Models;
using Serilog;

namespace Automaton.Runner.Console.Services;

public sealed class AppActionDeploymentService
{
    private readonly RunnerWorkspaceService workspace;
    private readonly AppActionExecutionCoordinator coordinator;
    private readonly ILogger logger = Log.ForContext<AppActionDeploymentService>();
    private readonly AppActionVersionStore versionStore;
    private readonly MachineActionDependencyPreparer? machineDependencyPreparer;

    public AppActionDeploymentService(
        RunnerWorkspaceService workspace,
        AppActionExecutionCoordinator? coordinator = null,
        MachineActionDependencyPreparer? machineDependencyPreparer = null)
    {
        this.workspace = workspace;
        this.coordinator = coordinator ?? new AppActionExecutionCoordinator();
        this.machineDependencyPreparer = machineDependencyPreparer;
        versionStore = new AppActionVersionStore(workspace);
    }

    public async Task<IReadOnlyList<AppActionDeploymentResult>> PrepareAllAsync(
        Guid appId,
        Guid deploymentTargetId,
        bool allowPrivilegedCapabilities = false,
        CancellationToken cancellationToken = default,
        ActionContainerPolicy? containerPolicy = null)
    {
        if (allowPrivilegedCapabilities && Automaton.Core.Enums.RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE")) != Automaton.Core.Enums.RunnerPurposes.Infrastructure)
            throw new UnauthorizedAccessException("Privileged Actions may only be prepared on Infrastructure Runners.");
        var actions = AppActionManifestParser.Discover(
            workspace.GetAppSourceRoot(appId, deploymentTargetId),
            allowPrivilegedCapabilities);
        if (actions.Count > 0) EnsureMachineActionPreparationAvailable(
            workspace.GetAppSourceRoot(appId, deploymentTargetId), containerPolicy);

        var results = new List<AppActionDeploymentResult>(actions.Count);
        foreach (var action in actions)
        {
            results.Add(await PrepareAsync(appId, deploymentTargetId, action, cancellationToken,
                containerPolicy));
        }

        return results;
    }

    internal void EnsureMachineActionPreparationAvailable(string sourceRoot, ActionContainerPolicy? policy)
    {
        if (RunnerApplicationProfiles.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"))
            != RunnerApplicationProfiles.MachineManagement)
            return;
        if (AppActionManifestParser.Discover(sourceRoot, allowPrivilegedCapabilities: false).Count == 0) return;
        if (machineDependencyPreparer is null)
            throw new InvalidOperationException("Bounded Action dependency preparation is unavailable on this Machine Management Runner.");
        MachineActionDependencyPreparer.EnsurePolicy(policy);
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
        => AppActionModuleSourcePreparer.PrepareAsync(request, cancellationToken,
            (appId, targetId, action, token) => PrepareAsync(appId, targetId, action, token));

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
        CancellationToken cancellationToken,
        ActionContainerPolicy? containerPolicy = null)
    {
        var machineManagement = RunnerApplicationProfiles.Normalize(
            Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"))
            == RunnerApplicationProfiles.MachineManagement;
        if (machineManagement) MachineActionDependencyPreparer.EnsurePolicy(containerPolicy);

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
        var stagingId = Guid.NewGuid();
        var stagingDirectory = workspace.GetAppActionStagingDirectory(appId, deploymentTargetId, action.Key, stagingId);
        Directory.CreateDirectory(actionRoot);
        try
        {
            AppActionDeploymentOutputGuard.EnsureNoLinks(action.DirectoryPath);
            AppActionSourceFiles.CopyToRuntime(action.DirectoryPath, stagingDirectory);
            AppActionDeploymentOutputGuard.EnsureNoLinks(stagingDirectory);
            var entrypoint = Path.GetFullPath(Path.Combine(stagingDirectory, action.Manifest.Spec.Entrypoint));
            AppActionSourceFiles.EnsureInside(stagingDirectory, entrypoint);
            if (!File.Exists(entrypoint))
                throw new InvalidOperationException($"Action '{action.Key}' entrypoint '{action.Manifest.Spec.Entrypoint}' does not exist.");

            if (action.Manifest.Spec.Runtime == AppActionRuntimes.Python)
            {
                if (machineManagement)
                {
                    if (machineDependencyPreparer is null)
                        throw new InvalidOperationException("Bounded Action dependency preparation is unavailable on this Machine Management Runner.");
                    await machineDependencyPreparer.PreparePythonAsync(appId, deploymentTargetId,
                        action.Key, stagingId, containerPolicy!, cancellationToken);
                }
                else
                    await AppActionRuntimePreparer.PreparePythonAsync(stagingDirectory, cancellationToken);
            }

            await versionStore.PublishAsync(appId, deploymentTargetId, action.Key,
                stagingDirectory, contentHash, cancellationToken);
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
