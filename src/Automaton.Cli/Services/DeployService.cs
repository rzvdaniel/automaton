using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public sealed class DeployService
{
    private const string AutomatonConfigFileName = "automaton.json";

    private readonly ConfigService _config;
    private readonly Func<string, string, CancellationToken, Task<int>> _runBuildAsync;
    private readonly Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>> _uploadSourceAsync;
    private readonly Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>> _uploadAsync;
    private readonly Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>> _uploadBackendAsync;
    private readonly Func<string, string, string, CancellationToken, Task<DeployBackendResult>> _deployBackendAsync;

    public DeployService(
        ConfigService config,
        Func<string, string, CancellationToken, Task<int>>? runBuildAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadSourceAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadBackendAsync = null,
        Func<string, string, string, CancellationToken, Task<DeployBackendResult>>? deployBackendAsync = null)
    {
        _config = config;
        _runBuildAsync = runBuildAsync ?? RunBuildAsync;
        _uploadSourceAsync = uploadSourceAsync ?? UploadSourceAsync;
        _uploadAsync = uploadAsync ?? UploadAsync;
        _uploadBackendAsync = uploadBackendAsync ?? UploadBackendAsync;
        _deployBackendAsync = deployBackendAsync ?? DeployBackendAsync;
    }

    public async Task<DeployResult> DeployAsync(
        string directory,
        bool clean,
        bool skipBackend,
        string? directoryOverride,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DeployCheckResult>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DeployUploadResult? sourceUploadResult = null;
        DeployUploadResult? uploadResult = null;
        DeployBackendResult? backendResult = null;

        var configPath = Path.Combine(directory, AutomatonConfigFileName);
        var configFileExists = File.Exists(configPath);
        var projectConfig = _config.TryLoadProjectConfig(directory);
        var (server, apiKey) = _config.ResolveCredentials(projectConfig, directory);

        var appLinkCheck = ValidateAppLink(configFileExists, projectConfig, nextSteps);
        checks.Add(appLinkCheck);

        var credentialsCheck = ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(credentialsCheck);

        if (!appLinkCheck.Success || !credentialsCheck.Success || projectConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, directoryOverride, sourceUploadResult, uploadResult, backendResult);
        }

        var buildCheck = await RunBuildIfNeededAsync(projectConfig.Frontend?.Build, directory, nextSteps, cancellationToken);
        checks.Add(buildCheck);

        if (!buildCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, directoryOverride, sourceUploadResult, uploadResult, backendResult);
        }

        var packageCheck = ValidateFrontendPackage(directory, directoryOverride ?? projectConfig.Frontend?.DeployDir, nextSteps, out var deployDirectory);
        checks.Add(packageCheck);

        if (!packageCheck.Success || deployDirectory == null)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, directoryOverride, sourceUploadResult, uploadResult, backendResult);
        }

        try
        {
            using var sourceZip = ZipService.CreateSourceZip(directory, projectConfig.Source);
            sourceUploadResult = await _uploadSourceAsync(server, apiKey, projectConfig.AppId, sourceZip, true, cancellationToken);
            checks.Add(new DeployCheckResult(
                DeployCheckNames.SourceUpload,
                true,
                $"uploaded {sourceUploadResult.Written} source file(s), {sourceUploadResult.Skipped} skipped ({sourceUploadResult.SizeMb:F1} MB)"));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Verify local source files and retry deploy so Studio source stays in sync");
            checks.Add(new DeployCheckResult(DeployCheckNames.SourceUpload, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        try
        {
            using var zip = ZipService.CreateZip(
                directory,
                deployDirectory,
                projectConfig.Frontend == null
                    ? null
                    : new DeployScopeConfig
                    {
                        Include = projectConfig.Frontend.Include,
                        Exclude = projectConfig.Frontend.Exclude
                    });
            uploadResult = await _uploadAsync(server, apiKey, projectConfig.AppId, zip, clean, cancellationToken);
            checks.Add(new DeployCheckResult(
                DeployCheckNames.Upload,
                true,
                $"uploaded {uploadResult.Written} file(s), {uploadResult.Skipped} skipped ({uploadResult.SizeMb:F1} MB)"));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            checks.Add(new DeployCheckResult(DeployCheckNames.Upload, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        var backendPackageCheck = ValidateBackendPackage(directory, projectConfig.Backend?.DeployDir, nextSteps, out var backendDirectory, out var backendControllers);
        checks.Add(backendPackageCheck);

        if (skipBackend)
        {
            backendResult = new DeployBackendResult(false, 0, "backend deploy skipped by flag");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendDeploy, true, backendResult.Message ?? "backend deploy skipped by flag"));
            return CreateResult(true, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        if (!backendPackageCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        if (backendControllers == 0 || backendDirectory == null)
        {
            backendResult = new DeployBackendResult(true, 0, "no backend controllers found");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendDeploy, true, backendResult.Message ?? "no backend controllers found"));
            return CreateResult(true, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        try
        {
            using var backendZip = ZipService.CreateZip(
                directory,
                backendDirectory,
                projectConfig.Backend == null
                    ? null
                    : new DeployScopeConfig
                    {
                        Include = projectConfig.Backend.Include,
                        Exclude = projectConfig.Backend.Exclude
                    });
            var backendUpload = await _uploadBackendAsync(server, apiKey, projectConfig.AppId, backendZip, true, cancellationToken);
            if (backendUpload.Written <= 0)
            {
                nextSteps.Add("Verify backend.deployDir files are not excluded and retry deploy");
                checks.Add(new DeployCheckResult(
                    DeployCheckNames.BackendPackage,
                    false,
                    $"backend upload wrote 0 files from {Path.GetRelativePath(directory, backendDirectory)}"));
                return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
            }

            checks.Add(new DeployCheckResult(
                DeployCheckNames.BackendPackage,
                true,
                $"uploaded {backendUpload.Written} backend file(s), {backendUpload.Skipped} skipped ({backendUpload.SizeMb:F1} MB)"));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Verify backend.deployDir in automaton.json and rerun automaton deploy");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendPackage, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        try
        {
            backendResult = await _deployBackendAsync(server, apiKey, projectConfig.AppId, cancellationToken);
        }
        catch (Exception ex)
        {
            nextSteps.Add("Inspect backend deploy logs in Automaton Studio");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendDeploy, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        if (!backendResult.Success || backendResult.RunnerCount <= 0)
        {
            nextSteps.Add("Rerun automaton deploy with --runner <runner-id-or-name>");
            nextSteps.Add("If no connected runner exists, create or deploy one in Automaton Studio, then rerun automaton deploy");
            checks.Add(new DeployCheckResult(
                DeployCheckNames.BackendDeploy,
                false,
                backendResult.Message ?? "backend deploy did not reach any runner"));
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
        }

        checks.Add(new DeployCheckResult(
            DeployCheckNames.BackendDeploy,
            true,
            backendResult.Message ?? $"backend deployed to {backendResult.RunnerCount} runner(s)"));

        return CreateResult(true, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendResult);
    }

    private static DeployCheckResult ValidateAppLink(bool configFileExists, Models.AutomatonConfig? projectConfig, ISet<string> nextSteps)
    {
        if (!configFileExists)
        {
            nextSteps.Add("Run automaton new \"My App\" or automaton link <app-id>");
            return new DeployCheckResult(DeployCheckNames.AppLink, false, "automaton.json not found in the project root");
        }

        if (projectConfig == null)
        {
            nextSteps.Add("Fix the automaton.json file so it contains valid JSON");
            return new DeployCheckResult(DeployCheckNames.AppLink, false, "automaton.json is invalid or unreadable");
        }

        if (string.IsNullOrWhiteSpace(projectConfig.AppId))
        {
            nextSteps.Add("Add appId to automaton.json or relink the app with automaton link <app-id>");
            return new DeployCheckResult(DeployCheckNames.AppLink, false, "automaton.json is missing appId");
        }

        return new DeployCheckResult(DeployCheckNames.AppLink, true, $"linked app {projectConfig.AppId}");
    }

    private static DeployCheckResult ValidateCredentials(string? server, string? apiKey, ISet<string> nextSteps)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            nextSteps.Add("Run automaton setup to configure the hosted server and credentials");
            return new DeployCheckResult(DeployCheckNames.Credentials, false, "server could not be resolved from environment, automaton.json, or CLI config");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            nextSteps.Add("Run automaton setup");
            return new DeployCheckResult(DeployCheckNames.Credentials, false, "API key could not be resolved from environment or CLI config");
        }

        return new DeployCheckResult(DeployCheckNames.Credentials, true, $"credentials resolved for {server}");
    }

    private async Task<DeployCheckResult> RunBuildIfNeededAsync(string? buildCommand, string directory, ISet<string> nextSteps, CancellationToken cancellationToken)
    {
        if (buildCommand is null)
        {
            return new DeployCheckResult(DeployCheckNames.Build, true, "no build configured");
        }

        if (string.IsNullOrWhiteSpace(buildCommand))
        {
            nextSteps.Add("Set a valid build command in automaton.json or remove the build field");
            return new DeployCheckResult(DeployCheckNames.Build, false, "build is configured in automaton.json but is empty");
        }

        var exitCode = await _runBuildAsync(buildCommand, directory, cancellationToken);
        if (exitCode != 0)
        {
            nextSteps.Add("Fix the build errors and rerun automaton deploy");
            return new DeployCheckResult(DeployCheckNames.Build, false, $"build failed with exit code {exitCode}");
        }

        return new DeployCheckResult(DeployCheckNames.Build, true, "build completed successfully");
    }

    private static DeployCheckResult ValidateFrontendPackage(string directory, string? deployDir, ISet<string> nextSteps, out string? resolvedDeployDirectory)
    {
        try
        {
            resolvedDeployDirectory = ZipService.ResolveDeployDirectory(directory, deployDir);
        }
        catch (Exception ex)
        {
            nextSteps.Add("Fix frontend.deployDir in automaton.json or pass --dir with a valid directory");
            resolvedDeployDirectory = null;
            return new DeployCheckResult(DeployCheckNames.FrontendPackage, false, ex.Message);
        }

        var indexPath = Path.Combine(resolvedDeployDirectory, "index.html");
        if (!File.Exists(indexPath))
        {
            nextSteps.Add("Ensure the deploy directory contains index.html before deploying");
            return new DeployCheckResult(
                DeployCheckNames.FrontendPackage,
                false,
                $"no index.html found in deploy directory: {Path.GetRelativePath(directory, resolvedDeployDirectory)}");
        }

        return new DeployCheckResult(
            DeployCheckNames.FrontendPackage,
            true,
            $"deploy directory ready: {Path.GetRelativePath(directory, resolvedDeployDirectory)}");
    }

    private static DeployCheckResult ValidateBackendPackage(
        string directory,
        string? deployDir,
        ISet<string> nextSteps,
        out string? resolvedBackendDirectory,
        out int backendControllers)
    {
        resolvedBackendDirectory = null;
        backendControllers = 0;

        var conventionalBackendDirectory = Path.Combine(directory, "backend");
        if (string.IsNullOrWhiteSpace(deployDir))
        {
            if (Directory.Exists(conventionalBackendDirectory)
                && Directory.GetFiles(conventionalBackendDirectory, "*.py", SearchOption.AllDirectories).Length > 0)
            {
                nextSteps.Add("Add backend.deployDir to automaton.json, usually 'backend'");
                return new DeployCheckResult(
                    DeployCheckNames.BackendPackage,
                    false,
                    "backend controllers were found, but backend.deployDir is missing from automaton.json");
            }

            return new DeployCheckResult(DeployCheckNames.BackendPackage, true, "no backend deploy configured");
        }

        try
        {
            resolvedBackendDirectory = ZipService.ResolveDeployDirectory(directory, deployDir);
        }
        catch (Exception ex)
        {
            nextSteps.Add("Fix backend.deployDir in automaton.json");
            return new DeployCheckResult(DeployCheckNames.BackendPackage, false, ex.Message);
        }

        backendControllers = Directory.GetFiles(resolvedBackendDirectory, "*.py", SearchOption.AllDirectories).Length;
        if (backendControllers == 0)
        {
            return new DeployCheckResult(
                DeployCheckNames.BackendPackage,
                true,
                $"backend directory ready but no Python controllers were found: {Path.GetRelativePath(directory, resolvedBackendDirectory)}");
        }

        return new DeployCheckResult(
            DeployCheckNames.BackendPackage,
            true,
            $"backend directory ready: {Path.GetRelativePath(directory, resolvedBackendDirectory)}");
    }

    private static DeployResult CreateResult(
        bool success,
        IReadOnlyList<DeployCheckResult> checks,
        IEnumerable<string> nextSteps,
        Models.AutomatonConfig? projectConfig,
        bool clean,
        bool skipBackend,
        string? deployDirectory,
        DeployUploadResult? sourceUploadResult,
        DeployUploadResult? uploadResult,
        DeployBackendResult? backendResult)
    {
        return new DeployResult
        {
            Success = success,
            Summary = success ? "Deploy complete" : "Deploy failed",
            AppId = projectConfig?.AppId,
            AppName = projectConfig?.Name,
            DeployDirectory = deployDirectory,
            Clean = clean,
            SkipBackend = skipBackend,
            Checks = checks,
            NextSteps = nextSteps.ToList(),
            SourceUploaded = sourceUploadResult,
            Uploaded = uploadResult,
            Backend = backendResult
        };
    }

    private static async Task<int> RunBuildAsync(string command, string workingDir, CancellationToken cancellationToken)
    {
        var (shell, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c {command}")
            : ("/bin/sh", $"-c \"{command}\"");

        var psi = new System.Diagnostics.ProcessStartInfo(shell, args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static async Task<DeployUploadResult> UploadSourceAsync(
        string server,
        string apiKey,
        string appId,
        Stream zip,
        bool clean,
        CancellationToken cancellationToken)
    {
        var sizeMb = zip.Length / 1_048_576.0;
        using var client = new AutomatonApiClient(server, apiKey);
        var uploadResult = await client.UploadSourceZipAsync(appId, zip, clean, cancellationToken);
        var written = uploadResult.TryGetProperty("written", out var writtenProperty)
            ? writtenProperty.GetArrayLength()
            : 0;
        var skipped = uploadResult.TryGetProperty("skipped", out var skippedProperty)
            ? skippedProperty.GetArrayLength()
            : 0;

        return new DeployUploadResult(written, skipped, sizeMb);
    }

    private static async Task<DeployUploadResult> UploadAsync(
        string server,
        string apiKey,
        string appId,
        Stream zip,
        bool clean,
        CancellationToken cancellationToken)
    {
        var sizeMb = zip.Length / 1_048_576.0;
        using var client = new AutomatonApiClient(server, apiKey);
        var uploadResult = await client.UploadZipAsync(appId, zip, clean, cancellationToken);
        var written = uploadResult.TryGetProperty("written", out var writtenProperty)
            ? writtenProperty.GetArrayLength()
            : 0;
        var skipped = uploadResult.TryGetProperty("skipped", out var skippedProperty)
            ? skippedProperty.GetArrayLength()
            : 0;

        return new DeployUploadResult(written, skipped, sizeMb);
    }

    private static async Task<DeployUploadResult> UploadBackendAsync(
        string server,
        string apiKey,
        string appId,
        Stream zip,
        bool clean,
        CancellationToken cancellationToken)
    {
        var sizeMb = zip.Length / 1_048_576.0;
        using var client = new AutomatonApiClient(server, apiKey);
        var uploadResult = await client.UploadBackendZipAsync(appId, zip, clean, cancellationToken);
        var written = uploadResult.TryGetProperty("written", out var writtenProperty)
            ? writtenProperty.GetArrayLength()
            : 0;
        var skipped = uploadResult.TryGetProperty("skipped", out var skippedProperty)
            ? skippedProperty.GetArrayLength()
            : 0;

        return new DeployUploadResult(written, skipped, sizeMb);
    }

    private static async Task<DeployBackendResult> DeployBackendAsync(
        string server,
        string apiKey,
        string appId,
        CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        var deployResult = await client.DeployBackendAsync(appId, cancellationToken);
        var success = deployResult.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();
        var message = deployResult.TryGetProperty("message", out var messageProperty) ? messageProperty.GetString() : null;
        var runnerCount = deployResult.TryGetProperty("runnerCount", out var runnerCountProperty) ? runnerCountProperty.GetInt32() : 0;
        return new DeployBackendResult(success, runnerCount, message);
    }
}
