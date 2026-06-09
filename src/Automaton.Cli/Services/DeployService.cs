using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public sealed class DeployService
{
    private const string AutomatonConfigFileName = "automaton.json";

    private readonly ConfigService _config;
    private readonly Action<string>? _progress;
    private readonly Action<DeployCheckResult>? _checkReported;
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
        : this(config, null, null, runBuildAsync, uploadSourceAsync, uploadAsync, uploadBackendAsync, deployBackendAsync)
    {
    }

    public DeployService(
        ConfigService config,
        Action<string>? progress,
        Func<string, string, CancellationToken, Task<int>>? runBuildAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadSourceAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadBackendAsync = null,
        Func<string, string, string, CancellationToken, Task<DeployBackendResult>>? deployBackendAsync = null)
        : this(config, progress, null, runBuildAsync, uploadSourceAsync, uploadAsync, uploadBackendAsync, deployBackendAsync)
    {
    }

    public DeployService(
        ConfigService config,
        Action<string>? progress = null,
        Action<DeployCheckResult>? checkReported = null,
        Func<string, string, CancellationToken, Task<int>>? runBuildAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadSourceAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadAsync = null,
        Func<string, string, string, Stream, bool, CancellationToken, Task<DeployUploadResult>>? uploadBackendAsync = null,
        Func<string, string, string, CancellationToken, Task<DeployBackendResult>>? deployBackendAsync = null)
    {
        _config = config;
        _progress = progress;
        _checkReported = checkReported;
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
        DeployUploadResult? backendUploadResult = null;
        DeployBackendResult? backendResult = null;

        var configPath = Path.Combine(directory, AutomatonConfigFileName);
        var configFileExists = File.Exists(configPath);
        var projectConfig = _config.TryLoadProjectConfig(directory);
        var (server, apiKey) = _config.ResolveCredentials(projectConfig, directory);

        var appLinkCheck = ValidateAppLink(configFileExists, projectConfig, nextSteps);
        AddCheck(checks, appLinkCheck);

        var credentialsCheck = ValidateCredentials(server, apiKey, nextSteps);
        AddCheck(checks, credentialsCheck);

        if (!appLinkCheck.Success || !credentialsCheck.Success || projectConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, directoryOverride, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        var buildCheck = await RunBuildIfNeededAsync(projectConfig.Frontend?.Build, directory, nextSteps, cancellationToken);
        AddCheck(checks, buildCheck);

        if (!buildCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, directoryOverride, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        var packageCheck = ValidateFrontendPackage(directory, directoryOverride ?? projectConfig.Frontend?.DeployDir, nextSteps, out var deployDirectory);
        AddCheck(checks, packageCheck);

        if (!packageCheck.Success || deployDirectory == null)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, directoryOverride, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        try
        {
            ReportProgress("Uploading source files...");
            using var sourceZip = ZipService.CreateSourceZip(directory, projectConfig.Source);
            sourceUploadResult = await _uploadSourceAsync(server, apiKey, projectConfig.AppId, sourceZip, true, cancellationToken);
            ReportProgress(FormatProgressMessage("Source upload complete", sourceUploadResult, "source file(s)"));
            AddCheck(checks, new DeployCheckResult(
                DeployCheckNames.SourceUpload,
                true,
                FormatUploadMessage("uploaded", "source file(s)", sourceUploadResult)));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Verify local source files and retry deploy so Studio source stays in sync");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.SourceUpload, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        try
        {
            ReportProgress($"Uploading frontend files from {Path.GetRelativePath(directory, deployDirectory)}...");
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
            ReportProgress(FormatProgressMessage("Frontend upload complete", uploadResult, "file(s)"));
            AddCheck(checks, new DeployCheckResult(
                DeployCheckNames.Upload,
                true,
                FormatUploadMessage("uploaded", "file(s)", uploadResult)));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.Upload, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        var backendPackageCheck = ValidateBackendPackage(directory, projectConfig.Backend?.DeployDir, nextSteps, out var backendDirectory, out var backendControllers);
        AddCheck(checks, backendPackageCheck);

        if (skipBackend)
        {
            backendResult = new DeployBackendResult(false, 0, "backend deploy skipped by flag");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.BackendDeploy, true, backendResult.Message ?? "backend deploy skipped by flag"));
            return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        if (!backendPackageCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        if (backendControllers == 0 || backendDirectory == null)
        {
            backendResult = new DeployBackendResult(true, 0, "no backend controllers found");
            checks.Add(new DeployCheckResult(DeployCheckNames.BackendDeploy, true, backendResult.Message ?? "no backend controllers found"));
            return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        try
        {
            ReportProgress($"Uploading backend files from {Path.GetRelativePath(directory, backendDirectory)}...");
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
            backendUploadResult = await _uploadBackendAsync(server, apiKey, projectConfig.AppId, backendZip, true, cancellationToken);
            if (backendUploadResult.Written <= 0)
            {
                nextSteps.Add("Verify backend.deployDir files are not excluded and retry deploy");
                AddCheck(checks, new DeployCheckResult(
                    DeployCheckNames.BackendPackage,
                    false,
                    $"backend upload wrote 0 files from {Path.GetRelativePath(directory, backendDirectory)}"));
                return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
            }

            ReportProgress(FormatProgressMessage("Backend upload complete", backendUploadResult, "backend file(s)"));
            AddCheck(checks, new DeployCheckResult(
                DeployCheckNames.BackendPackage,
                true,
                FormatUploadMessage("uploaded", "backend file(s)", backendUploadResult)));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Verify backend.deployDir in automaton.json and rerun automaton deploy");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.BackendPackage, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        try
        {
            ReportProgress("Requesting backend deploy on the assigned runner...");
            backendResult = await _deployBackendAsync(server, apiKey, projectConfig.AppId, cancellationToken);
        }
        catch (Exception ex)
        {
            nextSteps.Add("Inspect backend deploy logs in Automaton Studio");
            nextSteps.Add("Rerun automaton deploy with --runner <runner-id-or-name>");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.BackendDeploy, false, ex.Message, IsWarning: true));
            backendResult = new DeployBackendResult(false, 0, ex.Message);
            return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        if (!backendResult.Success || backendResult.RunnerCount <= 0)
        {
            nextSteps.Add("Rerun automaton deploy with --runner <runner-id-or-name>");
            nextSteps.Add("If no connected runner exists, create or deploy one in Automaton Studio, then rerun automaton deploy");
            AddCheck(checks, new DeployCheckResult(
                DeployCheckNames.BackendDeploy,
                false,
                backendResult.Message ?? "backend deploy did not reach any runner",
                IsWarning: true));
            ReportProgress("Backend deploy did not reach a connected runner.");
            return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
        }

        ReportProgress(backendResult.Message ?? $"Backend deploy reached {backendResult.RunnerCount} runner(s).");
        AddCheck(checks, new DeployCheckResult(
            DeployCheckNames.BackendDeploy,
            true,
            backendResult.Message ?? $"backend deployed to {backendResult.RunnerCount} runner(s)"));

        return CreateResult(true, checks, nextSteps, projectConfig, server, clean, skipBackend, deployDirectory, sourceUploadResult, uploadResult, backendUploadResult, backendResult);
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
            nextSteps.Add("Run automaton create to create and link a remote app, or automaton link <app-id> to link an existing app");
            return new DeployCheckResult(DeployCheckNames.AppLink, false, "automaton.json is missing appId; local scaffold exists but is not linked yet");
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
        string? server,
        bool clean,
        bool skipBackend,
        string? deployDirectory,
        DeployUploadResult? sourceUploadResult,
        DeployUploadResult? uploadResult,
        DeployUploadResult? backendUploadResult,
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
            OpenAppUrl = !string.IsNullOrWhiteSpace(projectConfig?.AppId)
                ? CliAppUrlBuilder.BuildDeployedUrl(server, projectConfig.AppId)
                : null,
            StudioAppUrl = !string.IsNullOrWhiteSpace(projectConfig?.AppId)
                ? CliAppUrlBuilder.BuildStudioAppUrl(server, projectConfig.AppId)
                : null,
            DeployDirectory = deployDirectory,
            Clean = clean,
            SkipBackend = skipBackend,
            Checks = checks,
            NextSteps = nextSteps.ToList(),
            SourceUploaded = sourceUploadResult,
            Uploaded = uploadResult,
            BackendUploaded = backendUploadResult,
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
        return ParseUploadResult(uploadResult, sizeMb);
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
        return ParseUploadResult(uploadResult, sizeMb);
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
        return ParseUploadResult(uploadResult, sizeMb);
    }

    private static DeployUploadResult ParseUploadResult(System.Text.Json.JsonElement uploadResult, double sizeMb)
    {
        var written = uploadResult.TryGetProperty("written", out var writtenProperty)
            ? writtenProperty.GetArrayLength()
            : 0;
        var skippedEntries = new List<DeploySkippedEntry>();
        if (uploadResult.TryGetProperty("skipped", out var skippedProperty) &&
            skippedProperty.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in skippedProperty.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var stringPath = item.GetString();
                    if (!string.IsNullOrWhiteSpace(stringPath))
                    {
                        skippedEntries.Add(new DeploySkippedEntry(stringPath!, "skipped", "unknown"));
                    }

                    continue;
                }

                if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    continue;
                }

                var skippedPath = item.TryGetProperty("path", out var pathProperty) ? pathProperty.GetString() ?? string.Empty : string.Empty;
                var reason = item.TryGetProperty("reason", out var reasonProperty) ? reasonProperty.GetString() ?? "skipped" : "skipped";
                var ruleSource = item.TryGetProperty("ruleSource", out var sourceProperty) ? sourceProperty.GetString() ?? "unknown" : "unknown";
                skippedEntries.Add(new DeploySkippedEntry(skippedPath, reason, ruleSource));
            }
        }

        return new DeployUploadResult(written, skippedEntries.Count, sizeMb, skippedEntries);
    }

    private static string FormatUploadMessage(string verb, string subject, DeployUploadResult result)
    {
        var message = $"{verb} {result.Written} {subject}, {result.Skipped} skipped ({result.SizeMb:F1} MB)";
        if (result.SkippedEntries == null || result.SkippedEntries.Count == 0)
        {
            return message;
        }

        var byRuleSource = result.SkippedEntries
            .GroupBy(entry => entry.RuleSource, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Count()} {group.Key}")
            .ToArray();

        return $"{message}; reasons: {string.Join(", ", byRuleSource)}";
    }

    private static string FormatProgressMessage(string prefix, DeployUploadResult result, string subject)
    {
        return $"{prefix} {result.Written} {subject}, {result.Skipped} skipped.";
    }

    private void ReportProgress(string message)
    {
        _progress?.Invoke(message);
    }

    private void AddCheck(ICollection<DeployCheckResult> checks, DeployCheckResult check)
    {
        checks.Add(check);
        _checkReported?.Invoke(check);
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
