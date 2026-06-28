using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public sealed class DeployService
{
    private const string AutomatonConfigFileName = "automaton.json";

    private readonly ConfigService config;
    private readonly Action<string>? progress;
    private readonly Action<DeployCheckResult>? checkReported;
    private readonly Func<string, string, CancellationToken, Task<int>> runBuildAsync;
    private readonly Func<string, string, string, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployBackendResult Deploy)>> deployPackageAsync;
    private readonly Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>> createSnapshotAsync;

    public DeployService(
        ConfigService config,
        Func<string, string, CancellationToken, Task<int>>? runBuildAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployBackendResult Deploy)>>? deployPackageAsync = null,
        Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>>? createSnapshotAsync = null)
        : this(config, null, null, runBuildAsync, deployPackageAsync, createSnapshotAsync)
    {
    }

    public DeployService(
        ConfigService config,
        Action<string>? progress,
        Func<string, string, CancellationToken, Task<int>>? runBuildAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployBackendResult Deploy)>>? deployPackageAsync = null,
        Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>>? createSnapshotAsync = null)
        : this(config, progress, null, runBuildAsync, deployPackageAsync, createSnapshotAsync)
    {
    }

    public DeployService(
        ConfigService config,
        Action<string>? progress = null,
        Action<DeployCheckResult>? checkReported = null,
        Func<string, string, CancellationToken, Task<int>>? runBuildAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployBackendResult Deploy)>>? deployPackageAsync = null,
        Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>>? createSnapshotAsync = null)
    {
        this.config = config;
        this.progress = progress;
        this.checkReported = checkReported;
        this.runBuildAsync = runBuildAsync ?? RunBuildAsync;
        this.deployPackageAsync = deployPackageAsync ?? DeployPackageAsync;
        this.createSnapshotAsync = createSnapshotAsync ?? CreateSnapshotAsync;
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
        DeployUploadResult? uploadResult = null;
        DeployBackendResult? backendResult = null;

        var configPath = Path.Combine(directory, AutomatonConfigFileName);
        var configFileExists = File.Exists(configPath);
        var projectConfig = config.TryLoadProjectConfig(directory);
        var (server, apiKey) = config.ResolveCredentials(projectConfig, directory);

        var appLinkCheck = ValidateAppLink(configFileExists, projectConfig, nextSteps);
        AddCheck(checks, appLinkCheck);

        var credentialsCheck = ValidateCredentials(server, apiKey, nextSteps);
        AddCheck(checks, credentialsCheck);

        if (!appLinkCheck.Success || !credentialsCheck.Success || projectConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, directoryOverride, uploadResult, backendResult);
        }

        var buildCheck = await RunBuildIfNeededAsync(projectConfig.Frontend?.Build, directory, nextSteps, cancellationToken);
        AddCheck(checks, buildCheck);
        if (!buildCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, directoryOverride, uploadResult, backendResult);
        }

        var frontendCheck = ValidateFrontendPackage(directory, directoryOverride ?? projectConfig.Frontend?.DeployDir, nextSteps, out var deployDirectory);
        AddCheck(checks, frontendCheck);
        if (!frontendCheck.Success || deployDirectory == null)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, directoryOverride, uploadResult, backendResult);
        }

        var backendCheck = ValidateBackendPackage(directory, projectConfig.Backend?.DeployDir, nextSteps, out var backendDirectory, out var backendControllers);
        AddCheck(checks, backendCheck);
        if (!skipBackend && !backendCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        try
        {
            ReportProgress("Creating restore point before deployment...");
            var snapshotResult = await createSnapshotAsync(server, apiKey, projectConfig.AppId, cancellationToken);
            AddCheck(checks, new DeployCheckResult("restore_point", snapshotResult.Created, snapshotResult.Message, IsWarning: !snapshotResult.Created));
            ReportProgress(snapshotResult.Message);
        }
        catch (Exception ex)
        {
            AddCheck(checks, new DeployCheckResult("restore_point", false, ex.Message, IsWarning: true));
            ReportProgress($"Restore point warning: {ex.Message}");
        }

        if (skipBackend)
        {
            ReportProgress("Creating deploy package...");
            using var zip = ZipService.CreateDeployPackageZip(
                directory,
                projectConfig.AppId,
                deployDirectory,
                null,
                projectConfig.Source,
                projectConfig.Frontend,
                null);
            ReportProgress("Uploading deploy package to the assigned runner...");
            var result = await deployPackageAsync(server, apiKey, projectConfig.AppId, zip, clean, cancellationToken);
            uploadResult = result.Upload;
            backendResult = new DeployBackendResult(true, result.Deploy.RunnerCount, "backend deploy skipped by flag");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageUpload, true, FormatUploadMessage("uploaded", "deploy package", uploadResult)));
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, true, "backend deploy skipped by flag"));
            return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        if (backendControllers == 0 || backendDirectory == null)
        {
            nextSteps.Add("Add backend.deployDir to automaton.json when the app gains a backend.");
            backendResult = new DeployBackendResult(true, 0, "no backend controllers found");
        }

        try
        {
            ReportProgress("Creating deploy package...");
            using var zip = ZipService.CreateDeployPackageZip(
                directory,
                projectConfig.AppId,
                deployDirectory,
                backendControllers > 0 ? backendDirectory : null,
                projectConfig.Source,
                projectConfig.Frontend,
                projectConfig.Backend);
            ReportProgress("Uploading deploy package to the assigned runner...");
            var result = await deployPackageAsync(server, apiKey, projectConfig.AppId, zip, clean, cancellationToken);
            uploadResult = result.Upload;
            backendResult = result.Deploy;
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageUpload, true, FormatUploadMessage("uploaded", "deploy package", uploadResult)));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Inspect runner deployment logs in Automaton Studio.");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageUpload, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        if (backendResult == null)
        {
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, false, "runner did not return a deployment result"));
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        if (!backendResult.Success || backendResult.RunnerCount <= 0)
        {
            nextSteps.Add("Rerun automaton deploy with --runner <runner-id-or-name>");
            nextSteps.Add("If no connected runner exists, create or deploy one in Automaton Studio, then rerun automaton deploy");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, false, backendResult.Message ?? "deploy package did not reach any runner", IsWarning: true));
            return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, true, backendResult.Message ?? $"deploy package applied to {backendResult.RunnerCount} runner(s)"));
        ReportProgress(backendResult.Message ?? $"Deploy package applied to {backendResult.RunnerCount} runner(s).");

        return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, uploadResult, backendResult);
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
            nextSteps.Add("Run automaton create or automaton link <app-id>");
            return new DeployCheckResult(DeployCheckNames.AppLink, false, "automaton.json is missing appId");
        }

        return new DeployCheckResult(DeployCheckNames.AppLink, true, $"linked app {projectConfig.AppId}");
    }

    private static DeployCheckResult ValidateCredentials(string? server, string? apiKey, ISet<string> nextSteps)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            nextSteps.Add("Run automaton login to configure the hosted server and credentials");
            return new DeployCheckResult(DeployCheckNames.Credentials, false, "server could not be resolved from environment, automaton.json, or CLI config");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            nextSteps.Add("Run automaton login");
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

        var exitCode = await runBuildAsync(buildCommand, directory, cancellationToken);
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
            return new DeployCheckResult(DeployCheckNames.FrontendPackage, false, $"no index.html found in deploy directory: {Path.GetRelativePath(directory, resolvedDeployDirectory)}");
        }

        return new DeployCheckResult(DeployCheckNames.FrontendPackage, true, $"deploy directory ready: {Path.GetRelativePath(directory, resolvedDeployDirectory)}");
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
                return new DeployCheckResult(DeployCheckNames.BackendPackage, false, "backend controllers were found, but backend.deployDir is missing from automaton.json");
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
        return new DeployCheckResult(
            DeployCheckNames.BackendPackage,
            true,
            backendControllers == 0
                ? $"backend directory ready but no Python controllers were found: {Path.GetRelativePath(directory, resolvedBackendDirectory)}"
                : $"backend directory ready: {Path.GetRelativePath(directory, resolvedBackendDirectory)}");
    }

    private static DeployResult CreateResult(
        bool success,
        IReadOnlyList<DeployCheckResult> checks,
        IEnumerable<string> nextSteps,
        Models.AutomatonConfig? projectConfig,
        string? server,
        bool clean,
        bool skipBackend,
        string? deployDirectory,
        DeployUploadResult? uploadResult,
        DeployBackendResult? backendResult)
    {
        return new DeployResult
        {
            Success = success,
            Summary = success
                ? checks.Any(check => !check.Success && check.IsWarning) ? "Deploy complete with warnings" : "Deploy complete"
                : "Deploy failed",
            AppId = projectConfig?.AppId,
            AppName = projectConfig?.Name,
            OpenAppUrl = !string.IsNullOrWhiteSpace(projectConfig?.AppId) ? CliAppUrlBuilder.BuildStudioApplicationUrl(server, projectConfig.AppId) : null,
            StudioAppUrl = !string.IsNullOrWhiteSpace(projectConfig?.AppId) ? CliAppUrlBuilder.BuildStudioAppUrl(server, projectConfig.AppId) : null,
            DeployDirectory = deployDirectory,
            Clean = clean,
            SkipBackend = skipBackend,
            Checks = checks,
            NextSteps = nextSteps.ToList(),
            Uploaded = uploadResult,
            Backend = backendResult,
            HasWarnings = checks.Any(check => !check.Success && check.IsWarning),
            BackendRunning = backendResult?.Success == true && backendResult.RunnerCount > 0
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

    private static async Task<(DeployUploadResult Upload, DeployBackendResult Deploy)> DeployPackageAsync(
        string server,
        string apiKey,
        string appId,
        Stream zip,
        bool clean,
        CancellationToken cancellationToken)
    {
        var sizeMb = zip.Length / 1_048_576.0;
        using var client = new AutomatonApiClient(server, apiKey);
        var deployResult = await client.DeployPackageAsync(appId, zip, clean, cancellationToken);
        var sourceFiles = deployResult.TryGetProperty("sourceFilesDeployed", out var sourceProperty) ? sourceProperty.GetInt32() : 0;
        var runtimeFiles = deployResult.TryGetProperty("runtimeFilesDeployed", out var runtimeProperty) ? runtimeProperty.GetInt32() : 0;
        var success = deployResult.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();
        var message = deployResult.TryGetProperty("message", out var messageProperty) ? messageProperty.GetString() : null;
        var runnerCount = deployResult.TryGetProperty("runnerCount", out var runnerCountProperty) ? runnerCountProperty.GetInt32() : 0;

        return (
            new DeployUploadResult(sourceFiles + runtimeFiles, 0, sizeMb),
            new DeployBackendResult(success, runnerCount, message));
    }

    private static string FormatUploadMessage(string verb, string subject, DeployUploadResult result)
    {
        return $"{verb} {result.Written} {subject}, {result.Skipped} ignored ({result.SizeMb:F1} MB)";
    }

    private void ReportProgress(string message)
    {
        progress?.Invoke(message);
    }

    private void AddCheck(ICollection<DeployCheckResult> checks, DeployCheckResult check)
    {
        checks.Add(check);
        checkReported?.Invoke(check);
    }

    private static async Task<(bool Created, string Message)> CreateSnapshotAsync(
        string server,
        string apiKey,
        string appId,
        CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        var snapshotResult = await client.CreateVersionSnapshotAsync(
            appId,
            trigger: "ManualSave",
            source: "CliDeploy",
            reason: "Before Deployment",
            ct: cancellationToken);

        var created = snapshotResult.TryGetProperty("snapshotCreated", out var createdProperty) && createdProperty.GetBoolean();
        var message = snapshotResult.TryGetProperty("message", out var messageProperty) ? messageProperty.GetString() : null;

        if (created && snapshotResult.TryGetProperty("version", out var versionProperty) &&
            versionProperty.ValueKind == System.Text.Json.JsonValueKind.Object &&
            versionProperty.TryGetProperty("versionNumber", out var versionNumberProperty))
        {
            return (true, message ?? $"Created restore point v{versionNumberProperty.GetInt32():D3} before deployment.");
        }

        return (false, message ?? "Skipped restore point creation because no source changes were detected.");
    }
}
