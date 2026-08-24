using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Serilog;

namespace Automaton.Runner.Console.Services;

public class RunnerDeploymentService
{
    private const long MaximumPackageBytes = 100L * 1024 * 1024;
    private const long MaximumExtractedBytes = 250L * 1024 * 1024;
    private const int MaximumArchiveEntries = 20_000;
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly RunnerBackendEnvironmentService backendEnvironmentService;
    private readonly IAppRuntimeProcessManager runtimeProcessManager;
    private readonly AppActionDeploymentService actionDeploymentService;
    private readonly ILogger logger;

    public RunnerDeploymentService(
        RunnerWorkspaceService runnerWorkspaceService,
        RunnerBackendEnvironmentService backendEnvironmentService,
        IAppRuntimeProcessManager runtimeProcessManager,
        AppActionDeploymentService actionDeploymentService)
    {
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.backendEnvironmentService = backendEnvironmentService;
        this.runtimeProcessManager = runtimeProcessManager;
        this.actionDeploymentService = actionDeploymentService;
        logger = Log.ForContext<RunnerDeploymentService>();
    }

    public async Task<DeployAppPackageResult> DeployPackage(DeployAppPackageRequest request)
    {
        var logs = new List<string>();
        var appId = request.AppId;
        var deploymentTargetId = request.DeploymentTargetId;
        var tempExtractRoot = Path.Combine(Path.GetTempPath(), "automaton-deploy", Guid.NewGuid().ToString("N"));
        string? snapshotPath = null;
        string? candidateRuntimeRoot = null;
        var candidatePromoted = false;
        var preserveCurrentRuntime = false;

        try
        {
            if (!File.Exists(request.PackagePath))
            {
                throw new FileNotFoundException("Deployment package was not found on the runner.", request.PackagePath);
            }
            if (new FileInfo(request.PackagePath).Length > MaximumPackageBytes)
                throw new InvalidOperationException("Deployment package exceeds the 100 MB limit.");

            Directory.CreateDirectory(tempExtractRoot);
            ExtractPackageSafely(request.PackagePath, tempExtractRoot);

            var manifest = LoadManifest(tempExtractRoot, appId);
            preserveCurrentRuntime = manifest.Application.Runtime.Command is not null;
            ValidateIncomingActions(tempExtractRoot, request.SystemInfrastructure);
            snapshotPath = CreateSnapshotIfNeeded(appId, deploymentTargetId);

            if (request.Clean)
            {
                if (manifest.Application.Runtime.Command is null)
                {
                    await runtimeProcessManager.StopAsync(appId, deploymentTargetId);
                    CleanDeploymentRoots(appId, deploymentTargetId);
                }
                else
                {
                    CleanDeploymentRoots(appId, deploymentTargetId, includeRuntime: false);
                }
            }

            SynchronizeActionRoots(appId, deploymentTargetId, tempExtractRoot);

            var sourceCount = CopyTreeIfExists(
                Path.Combine(tempExtractRoot, "source"),
                runnerWorkspaceService.GetAppSourceRoot(appId, deploymentTargetId));
            int runtimeCount;
            string? candidateArtifactRoot = null;
            if (manifest.Application.Runtime.Command is not null)
            {
                var candidateId = Guid.NewGuid();
                candidateRuntimeRoot = runnerWorkspaceService.GetAppRuntimeCandidateDirectory(
                    appId,
                    deploymentTargetId,
                    candidateId);
                candidateArtifactRoot = runnerWorkspaceService.GetAppRuntimeCandidateApplicationDirectory(
                    appId,
                    deploymentTargetId,
                    candidateId);
                runtimeCount = CopyTreeIfExists(
                    Path.Combine(tempExtractRoot, "runtime"),
                    candidateRuntimeRoot);
                Directory.CreateDirectory(candidateArtifactRoot);
            }
            else
            {
                runtimeCount = CopyTreeIfExists(
                    Path.Combine(tempExtractRoot, "runtime"),
                    runnerWorkspaceService.GetAppRuntimeRoot(appId, deploymentTargetId));
            }

            logs.Add($"Deployed {sourceCount} source file(s) and {runtimeCount} runtime file(s).");

            var actionResults = await actionDeploymentService.PrepareAllAsync(
                appId,
                deploymentTargetId,
                request.SystemInfrastructure);
            logs.AddRange(actionResults.Select(result => $"Action {result.ActionKey}: {result.Status}."));

            var runtimeBackendDir = runnerWorkspaceService.GetAppBackendDirectory(
                appId,
                deploymentTargetId);
            if (Directory.Exists(runtimeBackendDir))
            {
                var systemRequirementsPath = Path.Combine(runtimeBackendDir, "system-requirements.txt");
                if (File.Exists(systemRequirementsPath))
                {
                    logs.AddRange(backendEnvironmentService.InstallSystemPackages(
                        appId,
                        deploymentTargetId,
                        File.ReadAllText(systemRequirementsPath)));
                }

                var requirementsPath = Path.Combine(runtimeBackendDir, "requirements.txt");
                if (File.Exists(requirementsPath))
                {
                    logs.AddRange(backendEnvironmentService.InstallRequirements(
                        appId,
                        deploymentTargetId,
                        requirementsPath));
                }
                else if (manifest.HasBackend)
                {
                    logs.AddRange(backendEnvironmentService.EnsureAppVirtualEnvironment(
                        appId,
                        deploymentTargetId,
                        recreate: false));
                }
            }

            await runtimeProcessManager.ApplyAsync(
                appId,
                deploymentTargetId,
                manifest.Application,
                artifactRoot: candidateArtifactRoot);
            candidatePromoted = candidateArtifactRoot is not null;

            return new DeployAppPackageResult
            {
                DeploymentTargetId = request.DeploymentTargetId,
                Success = true,
                Message = $"Deployment package applied for app {appId}.",
                SourceFilesDeployed = sourceCount,
                RuntimeFilesDeployed = runtimeCount,
                SnapshotPath = snapshotPath,
                Logs = logs,
                Actions = actionResults.ToList()
            };
        }
        catch (Exception ex)
        {
            if (snapshotPath != null)
            {
                try
                {
                    RestoreSnapshot(
                        appId,
                        deploymentTargetId,
                        snapshotPath,
                        restoreRuntime: !preserveCurrentRuntime);
                    logs.Add("Deployment failed; the previous App snapshot was restored.");
                }
                catch (Exception restoreException)
                {
                    logger.Error(restoreException, "Failed to restore App snapshot after deployment failure for {AppId}", appId);
                    logs.Add("Deployment failed and automatic snapshot restoration also failed.");
                }
            }
            logger.Error(ex, "Failed to deploy app package for app {AppId}", appId);
            logs.Add(ex.Message);
            return new DeployAppPackageResult
            {
                DeploymentTargetId = request.DeploymentTargetId,
                Success = false,
                Message = ex.Message,
                Logs = logs
            };
        }
        finally
        {
            try
            {
                if (!candidatePromoted
                    && candidateRuntimeRoot is not null
                    && Directory.Exists(candidateRuntimeRoot))
                    Directory.Delete(candidateRuntimeRoot, recursive: true);
            }
            catch
            {
            }

            try
            {
                if (Directory.Exists(tempExtractRoot))
                {
                    Directory.Delete(tempExtractRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    public async Task CleanupTargetAsync(
        Guid appId,
        Guid deploymentTargetId)
    {
        await runtimeProcessManager.StopAsync(appId, deploymentTargetId);
        var targetRoot = runnerWorkspaceService.GetDeploymentTargetRoot(
            appId,
            deploymentTargetId);
        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, recursive: true);
        }
    }

    private AppDeploymentPackageManifest LoadManifest(string extractRoot, Guid requestAppId)
    {
        var manifestPath = Path.Combine(extractRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("Deployment package is missing manifest.json.");
        }

        var manifest = JsonSerializer.Deserialize<AppDeploymentPackageManifest>(File.ReadAllText(manifestPath));
        if (manifest == null || manifest.Version != 1 || string.IsNullOrWhiteSpace(manifest.AppId))
        {
            throw new InvalidOperationException("Deployment package manifest is invalid.");
        }

        if (!Guid.TryParse(manifest.AppId, out var manifestAppId)
            || manifestAppId != requestAppId)
        {
            throw new InvalidOperationException("Deployment package manifest app ID does not match the deployment request.");
        }

        ApplicationContractValidator.EnsureValid(manifest.Application);

        return manifest;
    }

    private string? CreateSnapshotIfNeeded(Guid appId, Guid deploymentTargetId)
    {
        var targetRoot = runnerWorkspaceService.GetDeploymentTargetRoot(appId, deploymentTargetId);
        if (!Directory.Exists(targetRoot))
        {
            return null;
        }

        var snapshotSourceRoots = new[]
        {
            runnerWorkspaceService.GetAppSourceRoot(appId, deploymentTargetId),
            runnerWorkspaceService.GetAppRuntimeRoot(appId, deploymentTargetId)
        };

        if (!snapshotSourceRoots.Any(Directory.Exists))
        {
            return null;
        }

        var snapshotRoot = Path.Combine(
            runnerWorkspaceService.GetAppVersionsDirectory(appId, deploymentTargetId),
            DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

        foreach (var root in snapshotSourceRoots.Where(Directory.Exists))
        {
            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            CopyDirectory(root, Path.Combine(snapshotRoot, name));
        }

        return snapshotRoot;
    }

    private void CleanDeploymentRoots(Guid appId, Guid deploymentTargetId, bool includeRuntime = true)
    {
        DeleteDirectoryIfExists(runnerWorkspaceService.GetAppSourceRoot(appId, deploymentTargetId));
        if (includeRuntime)
            DeleteDirectoryIfExists(runnerWorkspaceService.GetAppRuntimeRoot(appId, deploymentTargetId));
        DeleteDirectoryIfExists(runnerWorkspaceService.GetAppVenvDirectory(appId, deploymentTargetId));
    }

    private void RestoreSnapshot(
        Guid appId,
        Guid deploymentTargetId,
        string snapshotPath,
        bool restoreRuntime = true)
    {
        if (!Directory.Exists(snapshotPath))
            throw new DirectoryNotFoundException("The previous App snapshot is unavailable.");

        DeleteDirectoryIfExists(runnerWorkspaceService.GetAppSourceRoot(appId, deploymentTargetId));
        if (restoreRuntime)
            DeleteDirectoryIfExists(runnerWorkspaceService.GetAppRuntimeRoot(appId, deploymentTargetId));

        var snapshotSource = Path.Combine(snapshotPath, "source");
        var snapshotRuntime = Path.Combine(snapshotPath, "runtime");
        if (Directory.Exists(snapshotSource))
            CopyDirectory(snapshotSource, runnerWorkspaceService.GetAppSourceRoot(appId, deploymentTargetId));
        if (restoreRuntime && Directory.Exists(snapshotRuntime))
            CopyDirectory(snapshotRuntime, runnerWorkspaceService.GetAppRuntimeRoot(appId, deploymentTargetId));
    }

    private static void ValidateIncomingActions(string extractRoot, bool allowPrivilegedCapabilities)
    {
        var sourceRoot = Path.Combine(extractRoot, "source");
        foreach (var action in AppActionManifestParser.Discover(sourceRoot, allowPrivilegedCapabilities))
        {
            var entrypoint = Path.GetFullPath(Path.Combine(action.DirectoryPath, action.Manifest.Spec.Entrypoint));
            var actionRoot = Path.GetFullPath(action.DirectoryPath);
            if (!entrypoint.StartsWith(actionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(entrypoint))
                throw new InvalidOperationException($"Action '{action.Key}' entrypoint does not exist inside its Action folder.");
        }
    }

    private void SynchronizeActionRoots(Guid appId, Guid deploymentTargetId, string extractRoot)
    {
        var incomingRoot = Path.Combine(extractRoot, "source", "actions");
        // Deployment packages are complete source snapshots. A missing actions/
        // directory therefore means that the App now has zero Actions (ZIP files
        // cannot reliably represent empty directories).
        var incomingKeys = Directory.Exists(incomingRoot)
            ? Directory.GetDirectories(incomingRoot).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string?>(StringComparer.Ordinal);
        DeleteDirectoryIfExists(runnerWorkspaceService.GetAppSourceActionsDirectory(appId, deploymentTargetId));
        var runtimeRoot = runnerWorkspaceService.GetAppRuntimeActionsDirectory(appId, deploymentTargetId);
        if (!Directory.Exists(runtimeRoot)) return;
        foreach (var deployedDirectory in Directory.GetDirectories(runtimeRoot))
        {
            var key = Path.GetFileName(deployedDirectory);
            if (!incomingKeys.Contains(key)) Directory.Delete(deployedDirectory, recursive: true);
        }
    }

    private static int CopyTreeIfExists(string sourceRoot, string destinationRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return 0;
        }

        return CopyDirectory(sourceRoot, destinationRoot);
    }

    private static int CopyDirectory(string sourceRoot, string destinationRoot)
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void ExtractPackageSafely(string packagePath, string destinationRoot)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new InvalidOperationException($"Deployment package exceeds the {MaximumArchiveEntries} entry limit.");
        var totalBytes = 0L;
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
