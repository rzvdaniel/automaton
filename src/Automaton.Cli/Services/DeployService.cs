using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public sealed class DeployService
{
    private const string AutomatonConfigFileName = "automaton.json";

    private readonly ConfigService _config;
    private readonly Func<string, string, CancellationToken, Task<int>> _runBuildAsync;
    private readonly Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>> _uploadAsync;
    private readonly Func<string, string, string, CancellationToken, Task<DeployBackendResult>> _deployBackendAsync;

    public DeployService(
        ConfigService config,
        Func<string, string, CancellationToken, Task<int>>? runBuildAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadAsync = null,
        Func<string, string, string, CancellationToken, Task<DeployBackendResult>>? deployBackendAsync = null)
    {
        _config = config;
        _runBuildAsync = runBuildAsync ?? RunBuildAsync;
        _uploadAsync = uploadAsync ?? UploadAsync;
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
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, directoryOverride, uploadResult, backendResult);
        }

        var buildCheck = await RunBuildIfNeededAsync(projectConfig.Build, directory, nextSteps, cancellationToken);
        checks.Add(buildCheck);

        if (!buildCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, directoryOverride, uploadResult, backendResult);
        }

        var packageCheck = ValidateFrontendPackage(directory, directoryOverride ?? projectConfig.DeployDir, nextSteps, out var deployDirectory);
        checks.Add(packageCheck);

        if (!packageCheck.Success || deployDirectory == null)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, directoryOverride, uploadResult, backendResult);
        }

        try
        {
            using var zip = ZipService.CreateZip(directory, deployDirectory);
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
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        var backendControllers = CountBackendControllers(directory);

        if (skipBackend)
        {
            backendResult = new DeployBackendResult(false, 0, "backend deploy skipped by flag");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendDeploy, true, backendResult.Message ?? "backend deploy skipped by flag"));
            return CreateResult(true, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        if (backendControllers == 0)
        {
            backendResult = new DeployBackendResult(true, 0, "no backend controllers found");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendDeploy, true, backendResult.Message ?? "no backend controllers found"));
            return CreateResult(true, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        try
        {
            backendResult = await _deployBackendAsync(server, apiKey, projectConfig.AppId, cancellationToken);
        }
        catch (Exception ex)
        {
            nextSteps.Add("Inspect backend deploy logs in Automaton Studio");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendDeploy, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        if (!backendResult.Success || backendResult.RunnerCount <= 0)
        {
            nextSteps.Add("Run automaton runners");
            nextSteps.Add("Run automaton set-runner <runner-id>");
            checks.Add(new DeployCheckResult(
                DeployCheckNames.BackendDeploy,
                false,
                backendResult.Message ?? "backend deploy did not reach any runner"));
            return CreateResult(false, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, uploadResult, backendResult);
        }

        checks.Add(new DeployCheckResult(
            DeployCheckNames.BackendDeploy,
            true,
            backendResult.Message ?? $"backend deployed to {backendResult.RunnerCount} runner(s)"));

        return CreateResult(true, checks, nextSteps, projectConfig, clean, skipBackend, deployDirectory, uploadResult, backendResult);
    }

    private static DeployCheckResult ValidateAppLink(bool configFileExists, Models.AutomatonConfig? projectConfig, ISet<string> nextSteps)
    {
        if (!configFileExists)
        {
            nextSteps.Add("Run automaton create --name \"My App\" or automaton link <app-id>");
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
            nextSteps.Add("Configure the server with automaton config --server <url> or add server to automaton.json");
            return new DeployCheckResult(DeployCheckNames.Credentials, false, "server could not be resolved from environment, automaton.json, or CLI config");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            nextSteps.Add("Run automaton login or automaton config --key <api-key>");
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
            nextSteps.Add("Fix deployDir in automaton.json or pass --dir with a valid directory");
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

    private static int CountBackendControllers(string directory)
    {
        var backendDirectory = Path.Combine(directory, "backend");
        return Directory.Exists(backendDirectory)
            ? Directory.GetFiles(backendDirectory, "*.py", SearchOption.AllDirectories).Length
            : 0;
    }

    private static DeployResult CreateResult(
        bool success,
        IReadOnlyList<DeployCheckResult> checks,
        IEnumerable<string> nextSteps,
        Models.AutomatonConfig? projectConfig,
        bool clean,
        bool skipBackend,
        string? deployDirectory,
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
