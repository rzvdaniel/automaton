using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Serilog;

namespace Automaton.Runner.Console.Services;

public class RunnerDeploymentService
{
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly RunnerBackendEnvironmentService backendEnvironmentService;
    private readonly IAppBackendProcessManager backendProcessManager;
    private readonly ILogger logger;

    public RunnerDeploymentService(
        RunnerWorkspaceService runnerWorkspaceService,
        RunnerBackendEnvironmentService backendEnvironmentService,
        IAppBackendProcessManager backendProcessManager)
    {
        this.runnerWorkspaceService = runnerWorkspaceService;
        this.backendEnvironmentService = backendEnvironmentService;
        this.backendProcessManager = backendProcessManager;
        logger = Log.ForContext<RunnerDeploymentService>();
    }

    public async Task<DeployAppPackageResult> DeployPackage(DeployAppPackageRequest request)
    {
        var logs = new List<string>();
        var appId = request.AppId;
        var tempExtractRoot = Path.Combine(Path.GetTempPath(), "automaton-deploy", Guid.NewGuid().ToString("N"));

        try
        {
            if (!File.Exists(request.PackagePath))
            {
                throw new FileNotFoundException("Deployment package was not found on the runner.", request.PackagePath);
            }

            Directory.CreateDirectory(tempExtractRoot);
            ZipFile.ExtractToDirectory(request.PackagePath, tempExtractRoot);

            var manifest = LoadManifest(tempExtractRoot);
            var snapshotPath = CreateSnapshotIfNeeded(appId);

            if (request.Clean)
            {
                CleanDeploymentRoots(appId);
            }

            var sourceCount = CopyTreeIfExists(
                Path.Combine(tempExtractRoot, "source"),
                runnerWorkspaceService.GetAppSourceRoot(appId));
            var runtimeCount = CopyTreeIfExists(
                Path.Combine(tempExtractRoot, "runtime"),
                runnerWorkspaceService.GetAppRuntimeRoot(appId));

            logs.Add($"Deployed {sourceCount} source file(s) and {runtimeCount} runtime file(s).");

            var runtimeBackendDir = runnerWorkspaceService.GetAppBackendDirectory(appId);
            if (Directory.Exists(runtimeBackendDir))
            {
                var systemRequirementsPath = Path.Combine(runtimeBackendDir, "system-requirements.txt");
                if (File.Exists(systemRequirementsPath))
                {
                    logs.AddRange(backendEnvironmentService.InstallSystemPackages(appId, File.ReadAllText(systemRequirementsPath)));
                }

                var requirementsPath = Path.Combine(runtimeBackendDir, "requirements.txt");
                if (File.Exists(requirementsPath))
                {
                    logs.AddRange(backendEnvironmentService.InstallRequirements(appId, requirementsPath));
                }
                else if (manifest.HasBackend)
                {
                    logs.AddRange(backendEnvironmentService.EnsureAppVirtualEnvironment(appId, recreate: false));
                }
            }

            await PersistDeploymentMetadataAsync(appId, manifest);
            if (manifest.HasBackend)
                await backendProcessManager.EnsureRunningAsync(appId);
            else
                await backendProcessManager.StopAsync(appId);

            return new DeployAppPackageResult
            {
                Success = true,
                Message = $"Deployment package applied for app {appId}.",
                SourceFilesDeployed = sourceCount,
                RuntimeFilesDeployed = runtimeCount,
                SnapshotPath = snapshotPath,
                Logs = logs
            };
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to deploy app package for app {AppId}", appId);
            logs.Add(ex.Message);
            return new DeployAppPackageResult
            {
                Success = false,
                Message = ex.Message,
                Logs = logs
            };
        }
        finally
        {
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

    private async Task PersistDeploymentMetadataAsync(Guid appId, AppDeploymentPackageManifest manifest)
    {
        var metadataPath = Path.Combine(runnerWorkspaceService.GetAppRoot(appId), "deployment.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var metadata = JsonSerializer.Serialize(new
        {
            appId = appId.ToString(),
            hasBackend = manifest.HasBackend,
            backendModule = manifest.BackendModule,
            deployedAtUtc = DateTime.UtcNow
        });
        await File.WriteAllTextAsync(metadataPath, metadata);
    }

    private AppDeploymentPackageManifest LoadManifest(string extractRoot)
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

        return manifest;
    }

    private string? CreateSnapshotIfNeeded(Guid appId)
    {
        var appRoot = runnerWorkspaceService.GetAppRoot(appId);
        if (!Directory.Exists(appRoot))
        {
            return null;
        }

        var snapshotSourceRoots = new[]
        {
            runnerWorkspaceService.GetAppSourceRoot(appId),
            runnerWorkspaceService.GetAppRuntimeRoot(appId)
        };

        if (!snapshotSourceRoots.Any(Directory.Exists))
        {
            return null;
        }

        var snapshotRoot = Path.Combine(
            runnerWorkspaceService.GetAppVersionsDirectory(appId),
            DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

        foreach (var root in snapshotSourceRoots.Where(Directory.Exists))
        {
            var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            CopyDirectory(root, Path.Combine(snapshotRoot, name));
        }

        return snapshotRoot;
    }

    private void CleanDeploymentRoots(Guid appId)
    {
        DeleteDirectoryIfExists(runnerWorkspaceService.GetAppSourceRoot(appId));
        DeleteDirectoryIfExists(runnerWorkspaceService.GetAppRuntimeRoot(appId));
        DeleteDirectoryIfExists(runnerWorkspaceService.GetAppVenvDirectory(appId));
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
}
