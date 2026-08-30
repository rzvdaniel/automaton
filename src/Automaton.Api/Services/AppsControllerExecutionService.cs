using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using static Automaton.Api.Hubs.AutomatonHub;

namespace Automaton.Api.Services;

/// <summary>
/// Deploys backend scripts to runners and executes controller actions via HTTP tunnel.
/// </summary>
public class AppsControllerExecutionService
{
    private const string BackendRuntimeLevelInfo = "info";
    private const string BackendRuntimeLevelWarn = "warn";
    private const string BackendRuntimeLevelError = "error";
    private const string FlowArtifactsRelativeRoot = ".automaton/flows";
    private readonly ApplicationDbContext dataContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly TunnelRequestRegistry tunnelRegistry;
    private readonly FileUploadRegistry uploadRegistry;
    private readonly AppsTunnelConfig tunnelConfig;
    private readonly ApiInstanceIdentityService apiInstanceIdentityService;
    private readonly IAppDeployCapabilityService appDeployCapabilityService;
    private readonly UserImpersonationServiceFactory<FlowsService> flowsServiceFactory;
    private readonly BackendRouteConfigService backendRouteConfigService;
    private readonly ManagedRunnerBootstrapService managedRunnerBootstrapService;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly IAppDeploymentTargetResolver deploymentTargetResolver;
    private readonly AppDeploymentTargetStateService deploymentTargetStateService;
    private readonly Serilog.ILogger logger;

    public AppsControllerExecutionService(
        ApplicationDbContext dataContext,
        AppFileStorageService fileStorageService,
        IHubContext<AutomatonHub> automatonHub,
        TunnelRequestRegistry tunnelRegistry,
        FileUploadRegistry uploadRegistry,
        IOptions<AppsTunnelConfig> tunnelConfig,
        ApiInstanceIdentityService apiInstanceIdentityService,
        IAppDeployCapabilityService appDeployCapabilityService,
        UserImpersonationServiceFactory<FlowsService> flowsServiceFactory,
        BackendRouteConfigService backendRouteConfigService,
        ManagedRunnerBootstrapService managedRunnerBootstrapService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        IAppDeploymentTargetResolver deploymentTargetResolver,
        AppDeploymentTargetStateService deploymentTargetStateService)
    {
        this.dataContext = dataContext;
        this.fileStorageService = fileStorageService;
        this.automatonHub = automatonHub;
        this.tunnelRegistry = tunnelRegistry;
        this.uploadRegistry = uploadRegistry;
        this.tunnelConfig = tunnelConfig.Value;
        this.apiInstanceIdentityService = apiInstanceIdentityService;
        this.appDeployCapabilityService = appDeployCapabilityService;
        this.flowsServiceFactory = flowsServiceFactory;
        this.backendRouteConfigService = backendRouteConfigService;
        this.managedRunnerBootstrapService = managedRunnerBootstrapService;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.deploymentTargetResolver = deploymentTargetResolver;
        this.deploymentTargetStateService = deploymentTargetStateService;
        this.logger = Serilog.Log.ForContext<AppsControllerExecutionService>();
    }

    // ── Deploy ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight summary of what would be deployed for an app.
    /// Used by the frontend to show pre-deploy notifications (e.g. package installation warnings).
    /// </summary>
    public async Task<DeployInfoResponse> GetDeployInfoAsync(Guid appId, Guid ownerId)
    {
        _ = await fileStorageService.SyncSourceBackendToRuntimeBackendAsync(ownerId, appId);
        var scripts = await fileStorageService.ListRuntimeBackendFilesAsync(ownerId, appId);
        var hasRequirements = scripts.Any(s => s.ControllerName == "requirements" && s.Language == "Text");
        return new DeployInfoResponse
        {
            ScriptCount = scripts.Count,
            HasRequirements = hasRequirements
        };
    }

    /// <summary>
    /// Pushes all backend scripts from the server filesystem to the app's configured runner(s).
    /// Called by the "Deploy Backend" button and automatically on runner reconnect.
    /// </summary>
    public async Task<DeployBackendResponse> DeployBackendToRunnersAsync(
        Guid appId,
        Guid ownerId,
        string? targetSlug,
        List<string>? runnerNames,
        CancellationToken cancellationToken,
        bool manageTargetState = true)
    {
        if (string.IsNullOrWhiteSpace(targetSlug) && (runnerNames == null || runnerNames.Count == 0))
        {
            var bootstrap = await managedRunnerBootstrapService.EnsureRunnerAssignmentAsync(appId, ownerId, cancellationToken);
            if (!bootstrap.IsReady)
            {
                return new DeployBackendResponse
                {
                    Success = false,
                    State = bootstrap.State,
                    RequiresRetry = bootstrap.RequiresRetry,
                    Message = bootstrap.Message
                };
            }
        }

        var target = await deploymentTargetResolver.ResolveAsync(appId, targetSlug, ownerId, cancellationToken);
        if (manageTargetState)
        {
            await deploymentTargetStateService.MarkStartedAsync(appId, target.Id, cancellationToken);
        }

        try
        {
            _ = await fileStorageService.SyncSourceBackendToRuntimeBackendAsync(ownerId, appId);
            var scripts = await fileStorageService.ListRuntimeBackendFilesAsync(ownerId, appId);
            var hasRequirements = scripts.Any(s => s.ControllerName == "requirements" && s.Language == "Text");

            if (scripts.Count == 0)
            {
                var runnersWithoutScripts = (await ResolveRunnersByOwnerAsync(
                    ownerId, runnerNames, appId, target.Slug, cancellationToken)).ToList();
                if (runnersWithoutScripts.Count == 0)
                {
                    if (manageTargetState)
                        await deploymentTargetStateService.MarkCompletedAsync(appId, target.Id, success: true, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
                    return new DeployBackendResponse
                    {
                        DeploymentTargetId = target.Id,
                        TargetSlug = target.Slug,
                        Success = true,
                        DeployedCount = 0,
                        Message = "No backend scripts found"
                    };
                }

                await DeployManagedFlowArtifactsAsync(appId, target.Id, ownerId, runnersWithoutScripts, cancellationToken);
                if (manageTargetState)
                    await deploymentTargetStateService.MarkCompletedAsync(appId, target.Id, success: true, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
                return new DeployBackendResponse
                {
                    DeploymentTargetId = target.Id,
                    TargetSlug = target.Slug,
                    Success = true,
                    DeployedCount = 0,
                    RunnerCount = runnersWithoutScripts.Count,
                    Message = "No backend scripts found"
                };
            }

            var runners = await ResolveRunnersByOwnerAsync(
                ownerId, runnerNames, appId, target.Slug, cancellationToken);

            if (!runners.Any())
            {
                if (manageTargetState)
                    await deploymentTargetStateService.MarkCompletedAsync(appId, target.Id, success: false, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
                return new DeployBackendResponse
                {
                    DeploymentTargetId = target.Id,
                    TargetSlug = target.Slug,
                    Success = false,
                    TotalScripts = scripts.Count,
                    HasRequirements = hasRequirements,
                    Message = "No runners available for deployment. Please select a runner in the app settings."
                };
            }

            var storageBlock = await GetStorageDeploymentBlockAsync(runners, cancellationToken);
            if (storageBlock != null)
            {
                if (manageTargetState)
                    await deploymentTargetStateService.MarkCompletedAsync(appId, target.Id, success: false, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
                return new DeployBackendResponse
                {
                    DeploymentTargetId = target.Id,
                    TargetSlug = target.Slug,
                    Success = false,
                    TotalScripts = scripts.Count,
                    HasRequirements = hasRequirements,
                    Message = storageBlock
                };
            }

            var deployed = 0;
            var errors = new List<string>();
            var installLogs = new List<string>();

            foreach (var runner in runners)
            {
                foreach (var script in scripts)
                {
                    try
                    {
                        var payload = new
                        {
                            AppId = appId,
                            DeploymentTargetId = target.Id,
                            RelativePath = script.RelativePath,
                            ControllerName = script.ControllerName,
                            Content = script.Content,
                            Language = script.Language
                        };

                    var result = await automatonHub.Clients.Client(runner.ConnectionId)
                        .InvokeAsync<AppControllerUploadResult>(AutomatonHubMethods.UploadControllerScript, payload, cancellationToken);

                    ValidateDeploymentTargetResponse(target.Id, result.DeploymentTargetId);
                    if (result.Success)
                        {
                            deployed++;
                            if (result.Logs?.Count > 0)
                                installLogs.AddRange(result.Logs.Select(l => $"[{runner.Name}] {l}"));
                        }
                        else
                            errors.Add($"Runner '{runner.Name}' rejected {script.ControllerName}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Runner '{runner.Name}', script '{script.ControllerName}': {ex.Message}");
                        logger.Warning(ex, "Failed to deploy {ControllerName} to runner {RunnerName}", script.ControllerName, runner.Name);
                    }
                }

                try
                {
                    await DeployManagedFlowArtifactsAsync(appId, target.Id, ownerId, [runner], cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add($"Runner '{runner.Name}', flow artifacts: {ex.Message}");
                    logger.Warning(ex, "Failed to deploy flow artifacts for app {AppId} to runner {RunnerName}", appId, runner.Name);
                }
            }

            var response = new DeployBackendResponse
            {
                DeploymentTargetId = target.Id,
                TargetSlug = target.Slug,
                Success = !errors.Any(),
                DeployedCount = deployed,
                TotalScripts = scripts.Count,
                RunnerCount = runners.Count(),
                HasRequirements = hasRequirements,
                Message = errors.Any() ? string.Join("; ", errors) : $"Deployed {deployed} script(s) to {runners.Count()} runner(s)",
                InstallLogs = installLogs.Count > 0 ? installLogs : null
            };
            if (manageTargetState)
                await deploymentTargetStateService.MarkCompletedAsync(
                    appId, target.Id, response.Success, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
            return response;
        }
        catch
        {
            if (manageTargetState)
                await deploymentTargetStateService.MarkCompletedAsync(
                    appId, target.Id, success: false, cancellationToken: CancellationToken.None, legacyControllerRuntimeDeployed: true);
            throw;
        }
    }

    private async Task<string?> GetStorageDeploymentBlockAsync(IEnumerable<Runner> runners, CancellationToken cancellationToken)
    {
        var runnerIds = runners.Select(x => x.Id).ToArray();
        var hosted = await dataContext.HostedRunners
            .Where(x => x.RunnerId != null && runnerIds.Contains(x.RunnerId.Value) && x.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var runner in hosted)
        {
            if (runner.StorageState is HostedRunnerStorageStates.Healthy or HostedRunnerStorageStates.Warning)
                continue;
            if (runner.StorageState is HostedRunnerStorageStates.Unknown or HostedRunnerStorageStates.Stale or HostedRunnerStorageStates.MeasurementFailed)
            {
                var window = DateTime.UtcNow.Ticks / TimeSpan.FromHours(1).Ticks;
                var key = $"storage-verification:{runner.Id:N}:{window}";
                if (!await dataContext.HostedRunnerStorageVerifications.AnyAsync(x => x.IdempotencyKey == key, cancellationToken))
                {
                    dataContext.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
                    {
                        Id = Guid.NewGuid(),
                        HostedRunnerId = runner.Id,
                        IdempotencyKey = key,
                        Status = StorageVerificationStatuses.Pending,
                        Reason = "DeploymentGate",
                        RequestedAtUtc = DateTime.UtcNow
                    });
                    runner.StorageVerificationRequestedAtUtc = DateTime.UtcNow;
                    await dataContext.SaveChangesAsync(cancellationToken);
                }
                return "Runner storage usage is unavailable or stale. Verification has been requested; retry after it completes.";
            }
            return runner.StorageState == HostedRunnerStorageStates.OverLimit
                ? "Runner storage usage exceeds its monitored allowance. Remove data before deploying."
                : "Runner storage usage is too close to its monitored allowance for a storage-increasing deployment.";
        }
        return null;
    }

    public async Task<DeployPackageResponse> DeployPackageAsync(
        Guid appId,
        Guid ownerId,
        string? targetSlug,
        IFormFile? file,
        bool clean,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length <= 0)
        {
            throw new InvalidOperationException("A deployment package file is required.");
        }

        var packageApplication = await DeployPackageContractService.ReadAsync(file, appId, cancellationToken);

        if (string.IsNullOrWhiteSpace(targetSlug))
        {
            var bootstrap = await managedRunnerBootstrapService.EnsureRunnerAssignmentAsync(appId, ownerId, cancellationToken);
            if (!bootstrap.IsReady)
            {
                return new DeployPackageResponse
                {
                    Success = false,
                    State = bootstrap.State,
                    RequiresRetry = bootstrap.RequiresRetry,
                    Message = bootstrap.Message
                };
            }
        }

        var target = await deploymentTargetResolver.ResolveAsync(appId, targetSlug, ownerId, cancellationToken);
        await deploymentTargetStateService.MarkStartedAsync(appId, target.Id, cancellationToken);
        try
        {
            var runners = (await ResolveRunnersByOwnerAsync(
                ownerId, null, appId, target.Slug, cancellationToken)).ToList();
            if (runners.Count == 0)
            {
                throw new InvalidOperationException("No connected runner is assigned to this app.");
            }

            var runner = runners[0];
            var completion = await UploadPackageToRunnerAsync(appId, runner, file, cancellationToken);

            var result = await automatonHub.Clients.Client(runner.ConnectionId!)
                .InvokeAsync<DeployAppPackageResult>(
                    AutomatonHubMethods.DeployAppPackage,
                    new DeployAppPackageRequest
                    {
                        AppId = appId,
                        DeploymentTargetId = target.Id,
                        PackagePath = completion.LocalPath,
                        Clean = clean,
                        SystemInfrastructure = InfrastructureAppTrust.IsTrustedApp(appId)
                            && string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.Ordinal)
                    },
                    cancellationToken);

            ValidateDeploymentTargetResponse(target.Id, result.DeploymentTargetId);
            var genericRuntimeDeployed = result.Success && packageApplication.Runtime.Command is not null;
            await deploymentTargetStateService.MarkCompletedAsync(
                appId,
                target.Id,
                result.Success,
                cancellationToken: cancellationToken,
                genericRuntimeDeployed: genericRuntimeDeployed);

            if (result.Success)
            {
                await DeployPackageContractService.PersistAsync(
                    dataContext, appId, packageApplication, cancellationToken);
                // Keep API source as the compatibility copy used by Studio, pull,
                // remix, and fork flows. Runner-deployed Action content remains the
                // runtime authority and is never reconstructed from this copy.
                await PersistDeployPackageSourceAsync(appId, ownerId, file, cancellationToken);
                var deployedAtUtc = DateTime.UtcNow;
                await fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
                {
                    metadata.DevelopmentMode = AppDevelopmentModes.Local;
                    metadata.Frontend.RuntimeSource = AppRuntimeSources.CliBuild;
                    metadata.Frontend.RuntimeAvailable = metadata.Frontend.SourceAvailable || metadata.Frontend.RuntimeAvailable;
                    if (metadata.Frontend.RuntimeAvailable)
                    {
                        metadata.Frontend.RuntimeDeployedAtUtc = deployedAtUtc;
                    }
                    metadata.Backend.RuntimeSource = AppRuntimeSources.CliDeploy;
                    metadata.Backend.RuntimeAvailable = metadata.Backend.SourceAvailable || metadata.Backend.RuntimeAvailable;
                    if (metadata.Backend.RuntimeAvailable)
                    {
                        metadata.Backend.RuntimeDeployedAtUtc = deployedAtUtc;
                    }
                });

                await DeployManagedFlowArtifactsAsync(appId, target.Id, ownerId, [runner], cancellationToken);
                await backendRouteConfigService.RefreshAsync(cancellationToken);
            }

            return new DeployPackageResponse
            {
                DeploymentTargetId = target.Id,
                TargetSlug = target.Slug,
                Success = result.Success,
                RunnerCount = result.Success ? 1 : 0,
                SourceFilesDeployed = result.SourceFilesDeployed,
                RuntimeFilesDeployed = result.RuntimeFilesDeployed,
                SnapshotPath = result.SnapshotPath,
                Message = result.Message,
                Logs = result.Logs,
                Actions = result.Actions
            };
        }
        catch
        {
            await deploymentTargetStateService.MarkCompletedAsync(
                appId, target.Id, success: false, cancellationToken: CancellationToken.None, legacyControllerRuntimeDeployed: true);
            throw;
        }
    }

    public async Task<DeployAppResponse> DeployStudioAppAsync(
        Guid appId,
        Guid ownerId,
        string? targetSlug,
        CancellationToken cancellationToken)
    {
        var target = await deploymentTargetResolver.ResolveAsync(appId, targetSlug, ownerId, cancellationToken);
        await deploymentTargetStateService.MarkStartedAsync(appId, target.Id, cancellationToken);
        try
        {
            var capabilities = await appDeployCapabilityService.GetCapabilitiesAsync(ownerId, appId, cancellationToken);
            if (!capabilities.CanDeployAppFromStudio)
            {
                // Keep execution aligned with the capability contract. Even if a caller
                // bypasses the toolbar state, the backend must reject deploy-all when the
                // current Studio deploy implementation cannot fulfill it.
                // Keep server enforcement aligned with the Studio toolbar state. Even if a
                // caller bypasses the UI, the backend remains the source of truth for what
                // the current Studio deploy strategy can safely execute.
                throw new InvalidOperationException(capabilities.FrontendDeployBlockReason ?? "Studio cannot deploy this app.");
            }

            var frontendSync = await fileStorageService.SyncSourceFrontendToRuntimeFrontendAsync(ownerId, appId);
            var backendSync = await fileStorageService.SyncSourceBackendToRuntimeBackendAsync(ownerId, appId);
            var deployedAtUtc = DateTime.UtcNow;
            await fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
            {
                metadata.DevelopmentMode = AppDevelopmentModes.Studio;
                if (metadata.Frontend.SourceAvailable)
                {
                    metadata.Frontend.RuntimeSource = AppRuntimeSources.Studio;
                    metadata.Frontend.RuntimeAvailable = frontendSync.Updated || metadata.Frontend.RuntimeAvailable;
                    metadata.Frontend.RuntimeDeployedAtUtc = deployedAtUtc;
                }
                if (metadata.Backend.SourceAvailable)
                {
                    metadata.Backend.RuntimeSource = AppRuntimeSources.Studio;
                    metadata.Backend.RuntimeAvailable = backendSync.Updated || metadata.Backend.RuntimeAvailable;
                    metadata.Backend.RuntimeDeployedAtUtc = deployedAtUtc;
                }
            });

            var backendDeploy = await DeployBackendToRunnersAsync(
                appId, ownerId, target.Slug, null, cancellationToken, manageTargetState: false);
            // Studio operates deployed runtime state; Action source is deployed through
            // the CLI package path and is not copied from API-side source storage here.
            var actionDeployments = new List<AppActionDeploymentResult>();
            await backendRouteConfigService.RefreshAsync(cancellationToken);

            await deploymentTargetStateService.MarkCompletedAsync(
                appId, target.Id, backendDeploy.Success, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
            return new DeployAppResponse
            {
                DeploymentTargetId = target.Id,
                TargetSlug = target.Slug,
                Success = backendDeploy.Success,
                State = backendDeploy.State,
                RequiresRetry = backendDeploy.RequiresRetry,
                FrontendFilesSynced = frontendSync.CopiedFiles,
                BackendFilesSynced = backendSync.CopiedFiles,
                FrontendAction = frontendSync.Updated ? "updated" : "preserved",
                BackendAction = backendSync.Updated ? "updated" : "preserved",
                BackendDeploy = backendDeploy,
                Actions = actionDeployments,
                Message = $"Frontend {GetActionLabel(frontendSync.Updated, frontendSync.CopiedFiles)}; backend {GetActionLabel(backendSync.Updated, backendSync.CopiedFiles)}",
                FrontendRuntimeDeployedAtUtc = deployedAtUtc,
                BackendRuntimeDeployedAtUtc = deployedAtUtc
            };
        }
        catch
        {
            await deploymentTargetStateService.MarkCompletedAsync(
                appId, target.Id, success: false, cancellationToken: CancellationToken.None, legacyControllerRuntimeDeployed: true);
            throw;
        }
    }

    private async Task<List<AppActionDeploymentResult>> DeployStudioActionsAsync(Guid appId, Guid ownerId, CancellationToken cancellationToken)
    {
        var target = await deploymentTargetResolver.ResolveAsync(
            appId,
            targetSlug: null,
            ownerId,
            cancellationToken);
        var sourceRoot = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        var actions = AppActionManifestParser.Discover(sourceRoot);
        var runner = (await ResolveRunnersByOwnerAsync(ownerId, null, appId)).FirstOrDefault();
        if (runner == null || string.IsNullOrWhiteSpace(runner.ConnectionId))
            throw new InvalidOperationException("No connected Runner is assigned to this App.");
        var results = new List<AppActionDeploymentResult>(actions.Count);
        foreach (var action in actions)
        {
            var entrypoint = Path.GetFullPath(Path.Combine(action.DirectoryPath, action.Manifest.Spec.Entrypoint));
            if (!File.Exists(entrypoint)) throw new InvalidOperationException($"Action '{action.Key}' entrypoint does not exist.");
            var sourceZip = CreateActionSourceZip(action.DirectoryPath);
            var contentHash = ComputeActionContentHash(action.DirectoryPath);
            results.Add(await automatonHub.Clients.Client(runner.ConnectionId).InvokeAsync<AppActionDeploymentResult>(
                AutomatonHubMethods.PrepareAppActionModule,
                    new PrepareAppActionModuleRequest(
                        appId,
                        target.Id,
                        action.Key,
                        sourceZip,
                        contentHash,
                        SystemInfrastructure: false),
                cancellationToken));
        }
        await automatonHub.Clients.Client(runner.ConnectionId).InvokeAsync<int>(
            AutomatonHubMethods.SynchronizeAppActionModules,
                new SynchronizeAppActionModulesRequest(
                    appId,
                    target.Id,
                    actions.Select(action => action.Key).ToList()),
                cancellationToken);
        return results;
    }

    private static byte[] CreateActionSourceZip(string directory)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(file => !Path.GetRelativePath(directory, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment is ".venv" or "__pycache__")))
                archive.CreateEntryFromFile(file, Path.GetRelativePath(directory, file).Replace('\\', '/'), CompressionLevel.Optimal);
        }
        return stream.ToArray();
    }

    private static string ComputeActionContentHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(directory, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is ".venv" or "__pycache__"))
            .OrderBy(file => Path.GetRelativePath(directory, file), StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/')));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(file));
        }
        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    public async Task<DeployAppResponse> DeployFrontendAsync(
        Guid appId,
        Guid ownerId,
        string? targetSlug,
        CancellationToken cancellationToken)
    {
        var target = await deploymentTargetResolver.ResolveAsync(appId, targetSlug, ownerId, cancellationToken);
        await deploymentTargetStateService.MarkStartedAsync(appId, target.Id, cancellationToken);
        try
        {
            var capabilities = await appDeployCapabilityService.GetCapabilitiesAsync(ownerId, appId);
            if (!capabilities.CanDeployFrontendFromStudio)
            {
                throw new InvalidOperationException(capabilities.FrontendDeployBlockReason ?? "Studio cannot deploy this frontend.");
            }

            var frontendSync = await fileStorageService.SyncSourceFrontendToRuntimeFrontendAsync(ownerId, appId);
            var deployedAtUtc = DateTime.UtcNow;
            await fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
            {
                metadata.DevelopmentMode = AppDevelopmentModes.Studio;
                if (metadata.Frontend.SourceAvailable)
                {
                    metadata.Frontend.RuntimeSource = AppRuntimeSources.Studio;
                    metadata.Frontend.RuntimeAvailable = frontendSync.Updated || metadata.Frontend.RuntimeAvailable;
                    metadata.Frontend.RuntimeDeployedAtUtc = deployedAtUtc;
                }
            });

            await deploymentTargetStateService.MarkCompletedAsync(
                appId, target.Id, success: true, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
            await backendRouteConfigService.RefreshAsync(cancellationToken);
            return new DeployAppResponse
            {
                DeploymentTargetId = target.Id,
                TargetSlug = target.Slug,
                Success = true,
                FrontendFilesSynced = frontendSync.CopiedFiles,
                BackendFilesSynced = 0,
                FrontendAction = frontendSync.Updated ? "updated" : "preserved",
                BackendAction = "preserved",
                Message = $"Frontend {GetActionLabel(frontendSync.Updated, frontendSync.CopiedFiles)}; backend preserved existing live files",
                FrontendRuntimeDeployedAtUtc = deployedAtUtc
            };
        }
        catch
        {
            await deploymentTargetStateService.MarkCompletedAsync(
                appId, target.Id, success: false, cancellationToken: CancellationToken.None, legacyControllerRuntimeDeployed: true);
            throw;
        }
    }

    public async Task<DeployAppResponse> DeployBackendComponentAsync(
        Guid appId,
        Guid ownerId,
        string? targetSlug,
        CancellationToken cancellationToken)
    {
        var target = await deploymentTargetResolver.ResolveAsync(appId, targetSlug, ownerId, cancellationToken);
        await deploymentTargetStateService.MarkStartedAsync(appId, target.Id, cancellationToken);
        try
        {
            var capabilities = await appDeployCapabilityService.GetCapabilitiesAsync(ownerId, appId, cancellationToken);
            if (!capabilities.CanDeployBackendFromStudio)
            {
                throw new InvalidOperationException("Backend source is not available in Studio.");
            }

            var backendSync = await fileStorageService.SyncSourceBackendToRuntimeBackendAsync(ownerId, appId);
            var deployedAtUtc = DateTime.UtcNow;

            await fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
            {
                metadata.DevelopmentMode = AppDevelopmentModes.Studio;
                if (metadata.Backend.SourceAvailable)
                {
                    metadata.Backend.RuntimeSource = AppRuntimeSources.Studio;
                    metadata.Backend.RuntimeAvailable = backendSync.Updated || metadata.Backend.RuntimeAvailable;
                    metadata.Backend.RuntimeDeployedAtUtc = deployedAtUtc;
                }
            });

            var backendDeploy = await DeployBackendToRunnersAsync(
                appId, ownerId, target.Slug, null, cancellationToken, manageTargetState: false);
            await backendRouteConfigService.RefreshAsync(cancellationToken);

            await deploymentTargetStateService.MarkCompletedAsync(
                appId, target.Id, backendDeploy.Success, cancellationToken: cancellationToken, legacyControllerRuntimeDeployed: true);
            return new DeployAppResponse
            {
                DeploymentTargetId = target.Id,
                TargetSlug = target.Slug,
                Success = backendDeploy.Success,
                State = backendDeploy.State,
                RequiresRetry = backendDeploy.RequiresRetry,
                FrontendFilesSynced = 0,
                BackendFilesSynced = backendSync.CopiedFiles,
                FrontendAction = "preserved",
                BackendAction = backendSync.Updated ? "updated" : "preserved",
                BackendDeploy = backendDeploy,
                Message = $"Frontend preserved existing live files; backend {GetActionLabel(backendSync.Updated, backendSync.CopiedFiles)}",
                BackendRuntimeDeployedAtUtc = deployedAtUtc
            };
        }
        catch
        {
            await deploymentTargetStateService.MarkCompletedAsync(
                appId, target.Id, success: false, cancellationToken: CancellationToken.None, legacyControllerRuntimeDeployed: true);
            throw;
        }
    }

    public async Task<(byte[] Content, string ContentType)?> ReadRuntimeFrontendFileFromRunnerAsync(
        Guid appId,
        Guid ownerId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var target = await deploymentTargetResolver.ResolveAsync(
            appId,
            targetSlug: null,
            ownerId,
            cancellationToken);
        var runners = (await ResolveRunnersByOwnerAsync(ownerId, null, appId)).ToList();
        if (runners.Count == 0)
        {
            return null;
        }

        var body = JsonSerializer.Serialize(new
        {
            app_id = appId,
            deployment_target_id = target.Id,
            path = relativePath
        });

        var tunnelResponse = await SendTunnelRequestAsync(
            runners[0],
            method: "POST",
            path: "/api/runtime-file",
            body,
            cancellationToken);

        if (tunnelResponse.StatusCode == StatusCodes.Status404NotFound)
        {
            return null;
        }

        if (tunnelResponse.StatusCode < 200 || tunnelResponse.StatusCode >= 300)
        {
            throw new InvalidOperationException(tunnelResponse.Error ?? $"Runner returned HTTP {tunnelResponse.StatusCode}.");
        }

        var payload = JsonSerializer.Deserialize<RunnerRuntimeFileResponse>(
            tunnelResponse.Body ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (string.IsNullOrWhiteSpace(payload?.ContentBase64))
        {
            return null;
        }

        return (
            Convert.FromBase64String(payload.ContentBase64),
            string.IsNullOrWhiteSpace(payload.ContentType) ? "application/octet-stream" : payload.ContentType);
    }

    /// <summary>
    /// Deploys only Python controller scripts (skips requirements.txt / system-requirements.txt)
    /// to a specific runner. Fast — no pip/apt involved. Used for auto-deploy on first execution.
    /// </summary>
    private async Task DeployScriptsToRunnerAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid ownerId,
        Runner runner,
        CancellationToken cancellationToken)
    {
        _ = await fileStorageService.SyncSourceBackendToRuntimeBackendAsync(ownerId, appId);
        var scripts = await fileStorageService.ListRuntimeBackendFilesAsync(ownerId, appId);
        var pythonScripts = scripts.Where(s => s.Language != "Text").ToList();

        foreach (var script in pythonScripts)
        {
            try
            {
                var payload = new
                {
                    AppId = appId,
                    DeploymentTargetId = deploymentTargetId,
                    RelativePath = script.RelativePath,
                    ControllerName = script.ControllerName,
                    Content = script.Content,
                    Language = script.Language
                };

                await automatonHub.Clients.Client(runner.ConnectionId)
                    .InvokeAsync<AppControllerUploadResult>(AutomatonHubMethods.UploadControllerScript, payload, cancellationToken);

                logger.Debug("Auto-deployed {ControllerName} to runner {RunnerName}", script.ControllerName, runner.Name);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Auto-deploy failed for {ControllerName} on runner {RunnerName}", script.ControllerName, runner.Name);
            }
        }
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    public async Task<AppControllerActionResponse> ExecuteActionAsync(
        PublishedAppInfo app,
        string controllerName,
        string actionName,
        Dictionary<string, JsonElement> inputs,
        List<string>? runnerNames,
        bool useMockBackend,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var logs = new List<string>();

        try
        {
            if (useMockBackend)
            {
                var mockResult = await TryExecuteMockActionAsync(app, controllerName, actionName, inputs, logs, cancellationToken);
                if (mockResult != null)
                {
                    return mockResult;
                }
            }

            var runners = await ResolveRunnersAsync(app, runnerNames);

            if (!runners.Any())
                throw new InvalidOperationException("No runners available for execution");

            var results = await ExecuteViaTunnelAsync(app.Id, app.OwnerId ?? Guid.Empty, controllerName, actionName, inputs, runners, cancellationToken, logs);

            var success = results.All(r => r.Success);
            var firstSuccess = results.FirstOrDefault(r => r.Success);
            var errors = results.Where(r => !r.Success).Select(r => r.Error).ToList();

            if (!success && errors.Any(e => e != null))
            {
                foreach (var error in errors.Where(e => e != null))
                    logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Error: {error}");
            }

            if (!success)
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Execution failed");

            return new AppControllerActionResponse
            {
                Success = success,
                ControllerName = controllerName,
                ActionName = actionName,
                Status = success ? "Completed" : "Failed",
                RunnerCount = runners.Count(),
                ExecutedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                Data = firstSuccess?.Data,
                Logs = logs,
                Output = firstSuccess?.Output,
                Error = errors.Any() ? string.Join("; ", errors) : null
            };
        }
        catch (Exception ex)
        {
            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR: {ex.Message}");

            return new AppControllerActionResponse
            {
                Success = false,
                ControllerName = controllerName,
                ActionName = actionName,
                Status = "Failed",
                ExecutedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                Logs = logs,
                Output = null,
                Error = ex.Message
            };
        }
    }

    private async Task<AppControllerActionResponse?> TryExecuteMockActionAsync(
        PublishedAppInfo app,
        string controllerName,
        string actionName,
        Dictionary<string, JsonElement> inputs,
        List<string> logs,
        CancellationToken cancellationToken)
    {
        var ownerId = app.OwnerId;
        if (ownerId == null)
            return null;

        var mockContent = await fileStorageService.ReadSourceFileAsync(
            ownerId.Value,
            app.Id,
            $"backend\\mocks\\{controllerName}.{actionName}.json");

        if (string.IsNullOrWhiteSpace(mockContent))
        {
            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Mock not found for {controllerName}.{actionName}; falling back to runner execution");
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var mockDoc = JsonDocument.Parse(mockContent);
        var root = mockDoc.RootElement;

        var success = root.TryGetProperty("success", out var successElement)
            ? successElement.ValueKind == JsonValueKind.True
            : true;
        var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : null;
        var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : (success ? "Completed" : "Failed");
        var mockData = root.TryGetProperty("data", out var dataElement)
            ? dataElement.Clone()
            : (JsonElement?)null;

        logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Executed mock backend response from backend\\mocks\\{controllerName}.{actionName}.json");

        // Attach request inputs for visibility/debugging in preview logs
        if (inputs.Count > 0)
        {
            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Mock inputs: {JsonSerializer.Serialize(inputs)}");
        }

        return new AppControllerActionResponse
        {
            Success = success,
            ControllerName = controllerName,
            ActionName = actionName,
            Status = status ?? "Unknown",
            RunnerCount = 0,
            ExecutedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Data = mockData,
            Logs = logs,
            Output = null,
            Error = error
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<IEnumerable<Runner>> ResolveRunnersAsync(PublishedAppInfo app, List<string>? runnerNames)
    {
        var assignedRunner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
            app.Id,
            app.OwnerId ?? throw new InvalidOperationException("The App has no owner."),
            requireOnline: true);

        if (runnerNames is { Count: > 0 } &&
            !runnerNames.Contains(assignedRunner.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "App backend execution must use the assigned Runner.");
        }

        return [assignedRunner];
    }

    private async Task<IEnumerable<Runner>> ResolveRunnersByOwnerAsync(
        Guid ownerId,
        List<string>? runnerNames,
        Guid appId,
        string? targetSlug = null,
        CancellationToken cancellationToken = default)
    {
        var assignedRunner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
            appId,
            ownerId,
            requireOnline: true,
            targetSlug: targetSlug,
            cancellationToken: cancellationToken);

        if (runnerNames is { Count: > 0 } &&
            !runnerNames.Contains(assignedRunner.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "App backend deployment must use the assigned Runner.");
        }

        return [assignedRunner];
    }

    private static string GetActionLabel(bool updated, int fileCount) =>
        updated ? $"updated from source ({fileCount} file(s))" : "preserved existing live files";

    private async Task PersistDeployPackageSourceAsync(
        Guid appId,
        Guid ownerId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await fileStorageService.ClearSourceAsync(ownerId, appId);
        var sourceRoot = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        Directory.CreateDirectory(sourceRoot);

        using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var normalizedPath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (!normalizedPath.StartsWith("source/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativeSourcePath = normalizedPath["source/".Length..];
            if (string.IsNullOrWhiteSpace(relativeSourcePath))
            {
                continue;
            }

            AppFileStorageService.ValidateFileExtensionPublic(relativeSourcePath);

            var destinationPath = Path.GetFullPath(Path.Combine(sourceRoot, relativeSourcePath));
            var fullSourceRoot = Path.GetFullPath(sourceRoot);
            if (!destinationPath.StartsWith(fullSourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Deploy package source entry '{relativeSourcePath}' escapes the app source directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(destinationPath);
            await entryStream.CopyToAsync(outputStream, cancellationToken);
        }
    }

    private async Task<FileUploadCompleteMessage> UploadPackageToRunnerAsync(
        Guid appId,
        Runner runner,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 256 * 1024;
        var (sessionId, completionTask) = uploadRegistry.Register(TimeSpan.FromMinutes(10), cancellationToken);

        using var stream = file.OpenReadStream();
        var buffer = new byte[chunkSize];
        var chunkIndex = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var isLast = stream.Position >= stream.Length;
            var chunk = new FileUploadChunk
            {
                SessionId = sessionId,
                ChunkIndex = chunkIndex++,
                Data = bytesRead == chunkSize ? buffer : buffer[..bytesRead],
                IsLast = isLast,
                FileName = $"{appId:N}.zip",
                ContentType = "application/zip",
                AppId = appId
            };

            await automatonHub.Clients.Client(runner.ConnectionId!)
                .SendAsync(AutomatonHubMethods.ReceiveFileChunk, chunk, cancellationToken);
        }

        return await completionTask;
    }

    private async Task DeployManagedFlowArtifactsAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid ownerId,
        IEnumerable<Runner> runners,
        CancellationToken cancellationToken)
    {
        var metadata = await fileStorageService.GetAppStorageMetadataAsync(ownerId, appId);
        var flowIds = metadata.ConnectedFlowIds?
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : (Guid?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToList() ?? [];

        if (flowIds.Count == 0)
        {
            return;
        }

        var ownerFlowsService = flowsServiceFactory.CreateForUser(ownerId);
        var indexEntries = new List<object>();
        var artifacts = new List<(string RelativePath, byte[] Content)>();

        foreach (var flowId in flowIds)
        {
            var flow = ownerFlowsService.Get(flowId);
            if (flow == null)
            {
                logger.Warning("Connected flow {FlowId} for app {AppId} could not be loaded for deployment", flowId, appId);
                continue;
            }

            indexEntries.Add(new
            {
                FlowId = flow.Id,
                flow.Name
            });

            artifacts.Add((
                $"{FlowArtifactsRelativeRoot}/{flow.Id:N}.json",
                JsonSerializer.SerializeToUtf8Bytes(flow)));
        }

        artifacts.Add((
            $"{FlowArtifactsRelativeRoot}/index.json",
            JsonSerializer.SerializeToUtf8Bytes(indexEntries)));

        foreach (var runner in runners)
        {
            foreach (var artifact in artifacts)
            {
                var payload = new
                {
                    AppId = appId,
                    DeploymentTargetId = deploymentTargetId,
                    RelativePath = artifact.RelativePath,
                    ControllerName = "managed-flow-artifact",
                    Content = artifact.Content,
                    Language = "Text"
                };

                var result = await automatonHub.Clients.Client(runner.ConnectionId!)
                    .InvokeAsync<AppControllerUploadResult>(AutomatonHubMethods.UploadControllerScript, payload, cancellationToken);

                ValidateDeploymentTargetResponse(deploymentTargetId, result.DeploymentTargetId);
                if (!result.Success)
                {
                    throw new InvalidOperationException($"Runner '{runner.Name}' rejected backend file '{artifact.RelativePath}'.");
                }
            }
        }
    }

    private static void ValidateDeploymentTargetResponse(
        Guid expectedDeploymentTargetId,
        Guid actualDeploymentTargetId)
    {
        if (actualDeploymentTargetId != expectedDeploymentTargetId)
        {
            throw new InvalidOperationException(
                "Runner deployment response did not match the selected deployment target.");
        }
    }

    private async Task<List<RunnerAppControllerResult>> ExecuteViaTunnelAsync(
        Guid appId,
        Guid ownerId,
        string controllerName,
        string actionName,
        Dictionary<string, JsonElement> inputs,
        IEnumerable<Runner> runners,
        CancellationToken cancellationToken,
        List<string> logs)
    {
        var results = new List<RunnerAppControllerResult>();
        var target = await deploymentTargetResolver.ResolveAsync(
            appId,
            targetSlug: null,
            ownerId,
            cancellationToken);

        foreach (var runner in runners)
        {
            try
            {

                logger.Debug("Tunnel request: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName}",
                    appId, controllerName, actionName, runner.Name);

                var body = JsonSerializer.Serialize(new
                {
                    app_id = appId,
                    deployment_target_id = target.Id,
                    action = actionName,
                    inputs = inputs
                });

                var tunnelResponse = await SendTunnelRequestAsync(runner, controllerName, body, cancellationToken);

                // Auto-deploy on 404 "not deployed" — scripts missing from runner, push them and retry once
                if (tunnelResponse.StatusCode == 404)
                {
                    logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Controller not found on runner — auto-deploying scripts...");
                    logger.Warning("Auto-deploying scripts for AppId={AppId} to runner {RunnerName} (scripts not found on runner)", appId, runner.Name);

                    await DeployScriptsToRunnerAsync(
                        appId,
                        target.Id,
                        ownerId,
                        runner,
                        cancellationToken);

                    tunnelResponse = await SendTunnelRequestAsync(runner, controllerName, body, cancellationToken);
                }

                if (tunnelResponse.StatusCode >= 200 && tunnelResponse.StatusCode < 300)
                {
                    var payload = JsonSerializer.Deserialize<TunnelControllerResponse>(
                        tunnelResponse.Body ?? "{}",
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // Append any print() output from the controller action
                    if (!string.IsNullOrWhiteSpace(payload?.Output))
                    {
                        foreach (var line in payload.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] {line.TrimEnd()}");

                        await PublishBackendRuntimeOutputAsync(
                            appId,
                            target.Id,
                            controllerName,
                            actionName,
                            runner.Name,
                            payload.Output,
                            cancellationToken);
                    }

                    if (payload?.Success == true)
                        logger.Debug("Tunnel success: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName}",
                            appId, controllerName, actionName, runner.Name);
                    else
                        logger.Warning("Controller returned failure: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName} Error={Error}",
                            appId, controllerName, actionName, runner.Name, payload?.Error);

                    results.Add(new RunnerAppControllerResult
                    {
                        RunnerId = runner.Id,
                        RunnerName = runner.Name,
                        Success = payload?.Success ?? false,
                        Data = payload?.Data,
                        Error = payload?.Error,
                        Output = payload?.Output
                    });
                }
                else
                {
                    var errorDetail = tunnelResponse.Error ?? $"HTTP {tunnelResponse.StatusCode}";
                    logger.Warning("Tunnel HTTP error: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName} StatusCode={StatusCode} Error={Error}",
                        appId, controllerName, actionName, runner.Name, tunnelResponse.StatusCode, errorDetail);

                    logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR on {runner.Name}: {errorDetail}");
                    await PublishBackendRuntimeOutputAsync(
                        appId,
                        target.Id,
                        controllerName,
                        actionName,
                        runner.Name,
                        errorDetail,
                        cancellationToken);

                    results.Add(new RunnerAppControllerResult
                    {
                        RunnerId = runner.Id,
                        RunnerName = runner.Name,
                        Success = false,
                        Error = errorDetail
                    });
                }
            }
            catch (Exception ex)
            {
                logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] ERROR on {runner.Name}: {ex.Message}");
                await PublishBackendRuntimeOutputAsync(
                    appId,
                    target.Id,
                    controllerName,
                    actionName,
                    runner.Name,
                    ex.Message,
                    cancellationToken);
                logger.Error(ex, "Tunnel exception: AppId={AppId} Controller={ControllerName} Action={ActionName} Runner={RunnerName}",
                    appId, controllerName, actionName, runner.Name);

                results.Add(new RunnerAppControllerResult
                {
                    RunnerId = runner.Id,
                    RunnerName = runner.Name,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return results;
    }

    private Task<TunnelResponse> SendTunnelRequestAsync(Runner runner, string controllerName, string body, CancellationToken cancellationToken)
    {
        return SendTunnelRequestAsync(
            runner,
            method: "POST",
            path: $"/api/controller/{controllerName}",
            body,
            cancellationToken);
    }

    private async Task<TunnelResponse> SendTunnelRequestAsync(
        Runner runner,
        string method,
        string path,
        string? body,
        CancellationToken cancellationToken)
    {
        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            throw new InvalidOperationException($"Runner '{runner.Name}' is not connected");
        }

        if (!string.IsNullOrWhiteSpace(runner.ApiInstanceId)
            && !string.Equals(runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId, StringComparison.Ordinal))
        {
            logger.Warning(
                "Runner {RunnerName} belongs to API instance {RunnerApiInstanceId} color {RunnerApiColor}, but current request is handled by API instance {CurrentApiInstanceId} color {CurrentApiColor}",
                runner.Name,
                runner.ApiInstanceId,
                runner.ApiColor ?? "(unset)",
                apiInstanceIdentityService.ApiInstanceId,
                apiInstanceIdentityService.ApiColor ?? "(unset)");
        }

        var (requestId, responseTask) = tunnelRegistry.Register(
            timeout: TimeSpan.FromSeconds(tunnelConfig.TimeoutSeconds),
            ct: cancellationToken);

        var tunnelRequest = new TunnelRequest
        {
            RequestId = requestId,
            Method = method,
            Path = path,
            Body = body
        };

        await automatonHub.Clients
            .Client(runner.ConnectionId)
            .SendAsync(AutomatonHubMethods.TunnelRequest, tunnelRequest, cancellationToken);

        return await responseTask;
    }

    private async Task PublishBackendRuntimeOutputAsync(
        Guid appId,
        Guid deploymentTargetId,
        string controllerName,
        string actionName,
        string runnerName,
        string output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var lines = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        foreach (var line in lines)
        {
            var normalizedLevel = ClassifyBackendRuntimeLine(line);
            await automatonHub.Clients
                .Group(GetAppRuntimeLogsGroupName(appId, deploymentTargetId))
                .SendAsync(
                AutomatonHubMethods.BackendRuntimeLogEvent,
                new BackendRuntimeLogEvent
                {
                    AppId = appId,
                    DeploymentTargetId = deploymentTargetId,
                    Timestamp = DateTime.UtcNow,
                    Level = normalizedLevel,
                    Message = line,
                    Source = "backend",
                    Stream = normalizedLevel == BackendRuntimeLevelError ? "stderr" : "stdout",
                    ControllerName = controllerName,
                    ActionName = actionName,
                    RunnerName = runnerName
                },
                cancellationToken);
        }
    }

    private static string GetAppRuntimeLogsGroupName(
        Guid appId,
        Guid deploymentTargetId)
        => $"app-runtime-logs:{appId:D}:{deploymentTargetId:D}";

    private static string ClassifyBackendRuntimeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return BackendRuntimeLevelInfo;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Traceback", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Exception", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Exception:", StringComparison.OrdinalIgnoreCase))
        {
            return BackendRuntimeLevelError;
        }

        if (trimmed.StartsWith("WARN", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Warning:", StringComparison.OrdinalIgnoreCase))
        {
            return BackendRuntimeLevelWarn;
        }

        return BackendRuntimeLevelInfo;
    }

    private sealed class RunnerRuntimeFileResponse
    {
        public string? ContentBase64 { get; set; }
        public string? ContentType { get; set; }
    }
}
