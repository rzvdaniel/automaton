using System.Text.Json;
using Automaton.Cli.Models;
using Automaton.Core.Models;
using Automaton.Core.Services;

namespace Automaton.Cli.Services;

public sealed class DeployService
{
    private const string AutomatonConfigFileName = "automaton.json";
    private const string DeployStatePreparingManagedRuntime = "preparingManagedRuntime";
    private const string DeployStateManagedRunnerProvisionFailed = "managedRunnerProvisionFailed";
    private const string DeployStateManagedRunnerWaitingForConnection = "managedRunnerWaitingForConnection";
    private const string DeployStateManagedRuntimeUnavailable = "managedRuntimeUnavailable";
    private const string DeployStateReady = "ready";
    private static readonly TimeSpan ManagedRuntimeTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ManagedRuntimePollInterval = TimeSpan.FromSeconds(3);

    private readonly ConfigService config;
    private readonly Action<string>? progress;
    private readonly Action<DeployCheckResult>? checkReported;
    private readonly Func<string, string, string, string?, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)>> deployPackageAsync;
    private readonly Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>> createSnapshotAsync;
    private readonly Func<string, string, string, CancellationToken, Task<JsonElement>>? ensureManagedRuntimeReadyAsync;

    public DeployService(
        ConfigService config,
        Func<string, string, string, string?, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)>> deployPackageAsync,
        Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>>? createSnapshotAsync = null)
        : this(config, null, null, deployPackageAsync, createSnapshotAsync)
    {
    }

    public DeployService(
        ConfigService config,
        Action<string>? progress,
        Func<string, string, string, string?, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)>>? deployPackageAsync = null,
        Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>>? createSnapshotAsync = null)
        : this(config, progress, null, deployPackageAsync, createSnapshotAsync)
    {
    }

    public DeployService(
        ConfigService config,
        Action<string>? progress = null,
        Action<DeployCheckResult>? checkReported = null,
        Func<string, string, string, string?, Stream, bool, CancellationToken, Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)>>? deployPackageAsync = null,
        Func<string, string, string, CancellationToken, Task<(bool Created, string Message)>>? createSnapshotAsync = null,
        Func<string, string, string, CancellationToken, Task<JsonElement>>? ensureManagedRuntimeReadyAsync = null)

    {
        this.config = config;
        this.progress = progress;
        this.checkReported = checkReported;
        this.deployPackageAsync = deployPackageAsync ?? DeployPackageAsync;
        this.createSnapshotAsync = createSnapshotAsync ?? CreateSnapshotAsync;
        this.ensureManagedRuntimeReadyAsync = ensureManagedRuntimeReadyAsync
            ?? (deployPackageAsync == null ? EnsureManagedRuntimeReadyAsync : null);
    }

    public async Task<DeployResult> DeployAsync(
        string directory,
        bool clean,
        string? directoryOverride,
        string? environmentName = null,
        DeploymentTargetInfo? target = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DeployCheckResult>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DeployUploadResult? uploadResult = null;
        DeployRuntimeResult? runtimeResult = null;

        var configPath = Path.Combine(directory, AutomatonConfigFileName);
        var configFileExists = File.Exists(configPath);
        var projectConfig = config.TryLoadProjectConfig(directory);
        var resolved = config.ResolveContext(projectConfig, directory, environmentName);
        var server = resolved.Server;
        var apiKey = resolved.ApiKey;
        var appId = resolved.AppId;

        var appLinkCheck = ValidateAppLink(configFileExists, projectConfig, appId, nextSteps);
        AddCheck(checks, appLinkCheck);

        var credentialsCheck = ValidateCredentials(server, apiKey, nextSteps);
        AddCheck(checks, credentialsCheck);

        if (!appLinkCheck.Success || !credentialsCheck.Success || projectConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, directoryOverride, uploadResult, runtimeResult, target);
        }

        var deploymentApplication = ResolveDeploymentApplication(projectConfig.Application, directoryOverride);
        var buildCheck = ValidateBuildForRunner(deploymentApplication.Build, directory, nextSteps);
        AddCheck(checks, buildCheck);
        if (!buildCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, directoryOverride, uploadResult, runtimeResult, target);
        }

        var actionsCheck = ValidateActions(directory, nextSteps, out var actionCount);
        AddCheck(checks, actionsCheck);
        if (!actionsCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, directoryOverride, uploadResult, runtimeResult, target);
        }

        var applicationCheck = ValidateApplicationPackage(
            directory,
            ApplicationBuildArtifactSelector.GetRelativeDirectory(deploymentApplication),
            actionCount > 0,
            deploymentApplication.Runtime.Command is not null,
            deploymentApplication.Build.Command is not null,
            nextSteps,
            out var deployDirectory);
        AddCheck(checks, applicationCheck);
        if (!applicationCheck.Success)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, directoryOverride, uploadResult, runtimeResult, target);
        }

        try
        {
            ReportProgress("Creating restore point before deployment...");
            var snapshotResult = await createSnapshotAsync(server, apiKey, appId!, cancellationToken);
            AddCheck(checks, new DeployCheckResult("restore_point", snapshotResult.Created, snapshotResult.Message, IsWarning: !snapshotResult.Created));
            ReportProgress(snapshotResult.Message);
        }
        catch (Exception ex)
        {
            AddCheck(checks, new DeployCheckResult("restore_point", false, ex.Message, IsWarning: true));
            ReportProgress($"Restore point warning: {ex.Message}");
        }

        // Every deployed app uses the configured generic runtime contract. The
        // runner assignment is not inferred from source files or technology.
        if (ensureManagedRuntimeReadyAsync != null && target?.IsDefault != false)
        {
            var readiness = await WaitForManagedRuntimeAsync(server!, apiKey!, appId!, cancellationToken);
            if (!readiness.Ready)
            {
                nextSteps.Add(readiness.NextStep);
                AddCheck(checks, new DeployCheckResult(DeployCheckNames.RunnerAssignment, false, readiness.Message));
                if (readiness.State is DeployStateManagedRunnerProvisionFailed
                    or DeployStateManagedRunnerWaitingForConnection
                    or DeployStateManagedRuntimeUnavailable
                    or DeployStatePreparingManagedRuntime)
                {
                    AddManagedRuntimeNextSteps(
                        nextSteps,
                        new DeployRuntimeResult(false, 0, readiness.Message, readiness.State, RequiresRetry: true));
                }

                runtimeResult = new DeployRuntimeResult(false, 0, readiness.Message, readiness.State, RequiresRetry: true);
                return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, deployDirectory, uploadResult, runtimeResult, target);
            }
        }

        try
        {
            ReportProgress("Creating deploy package...");
            using var zip = ZipService.CreateDeployPackageZip(
                directory,
                appId!,
                deploymentApplication,
                projectConfig.Source,
                deployDirectory);
            ReportProgress("Uploading deploy package to the assigned runner...");
            var result = await deployPackageAsync(
                server, apiKey, appId!, target?.IsDefault == true ? null : target?.Slug, zip, clean, cancellationToken);
            uploadResult = result.Upload;
            runtimeResult = result.Deploy;
            ReportDeploymentLogs(runtimeResult.Logs);
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageUpload, true, FormatUploadMessage("uploaded", "deploy package", uploadResult)));
        }
        catch (Exception ex)
        {
            nextSteps.Add("Inspect runner deployment logs in Automaton Studio.");
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageUpload, false, ex.Message));
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, deployDirectory, uploadResult, runtimeResult, target);
        }

        if (runtimeResult == null)
        {
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, false, "runner did not return a deployment result"));
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, deployDirectory, uploadResult, runtimeResult, target);
        }

        if (!runtimeResult.Success || runtimeResult.RunnerCount <= 0)
        {
            AddManagedRuntimeNextSteps(nextSteps, runtimeResult);
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, false, runtimeResult.Message ?? "deploy package did not reach any runner", IsWarning: true));
            return CreateResult(true, checks, nextSteps, projectConfig, appId, server, clean, deployDirectory, uploadResult, runtimeResult, target);
        }

        AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, true, runtimeResult.Message ?? $"deploy package applied to {runtimeResult.RunnerCount} runner(s)"));
        ReportProgress(runtimeResult.Message ?? $"Deploy package applied to {runtimeResult.RunnerCount} runner(s).");

        return CreateResult(
            true, checks, nextSteps, projectConfig, appId, server, clean,
            deployDirectory, uploadResult, runtimeResult, target);
    }

    private async Task<(bool Ready, string Message, string NextStep, string State)> WaitForManagedRuntimeAsync(
        string server,
        string apiKey,
        string appId,
        CancellationToken cancellationToken)
    {
        ReportProgress("Managed runtime is being prepared...");
        var deadline = DateTime.UtcNow + ManagedRuntimeTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var readiness = await ensureManagedRuntimeReadyAsync!(server, apiKey, appId, cancellationToken);
            var ready = readiness.TryGetProperty("isReady", out var readyProperty) && readyProperty.GetBoolean();
            if (ready)
            {
                ReportProgress("Managed runner connected.");
                return (true, "Managed runner connected.", string.Empty, DeployStateReady);
            }

            var state = readiness.TryGetProperty("state", out var stateProperty) ? stateProperty.GetString() : null;
            var message = readiness.TryGetProperty("message", out var messageProperty)
                ? messageProperty.GetString() ?? "Managed runtime is being prepared."
                : "Managed runtime is being prepared.";
            if (string.Equals(state, DeployStateManagedRuntimeUnavailable, StringComparison.Ordinal)
                || string.Equals(state, DeployStateManagedRunnerProvisionFailed, StringComparison.Ordinal))
            {
                return (false, message, message, state ?? DeployStateManagedRuntimeUnavailable);
            }

            ReportProgress("Waiting for managed runner...");
            await Task.Delay(ManagedRuntimePollInterval, cancellationToken);
        }

        return (false,
            "Managed runner did not become ready before the timeout.",
            "Managed runner did not connect within 5 minutes. Rerun automaton deploy to try again.",
            DeployStateManagedRunnerWaitingForConnection);
    }

    private static DeployCheckResult ValidateAppLink(bool configFileExists, Models.AutomatonConfig? projectConfig, string? appId, ISet<string> nextSteps)
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

        if (string.IsNullOrWhiteSpace(appId))
        {
            nextSteps.Add("Run automaton create or automaton link <app-id> with --env <name>");
            return new DeployCheckResult(DeployCheckNames.AppLink, false, "the selected environment is not linked to an appId");
        }

        return new DeployCheckResult(DeployCheckNames.AppLink, true, $"linked app {appId}");
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

    private static DeployCheckResult ValidateBuildForRunner(ApplicationBuildSpec build, string directory, ISet<string> nextSteps)
    {
        if (build.Command is null)
        {
            return new DeployCheckResult(DeployCheckNames.Build, true, "no build configured");
        }

        try
        {
            ApplicationContractValidator.EnsureValid(new ApplicationContract { Build = build });
            var workingDirectory = ApplicationBuildExecutor.ResolveWorkspacePath(
                directory,
                build.Command.WorkingDirectory,
                "build working directory");
            if (!Directory.Exists(workingDirectory))
                throw new DirectoryNotFoundException($"Build working directory does not exist: {workingDirectory}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException)
        {
            nextSteps.Add("Fix the application build contract and rerun automaton deploy");
            return new DeployCheckResult(DeployCheckNames.Build, false, ex.Message);
        }

        return new DeployCheckResult(DeployCheckNames.Build, true, "build deferred to the assigned Runner");
    }

    private static DeployCheckResult ValidateApplicationPackage(
        string directory,
        string? deployDir,
        bool hasActions,
        bool hasRuntime,
        bool buildOnRunner,
        ISet<string> nextSteps,
        out string? resolvedDeployDirectory)
    {
        if (!hasRuntime)
        {
            resolvedDeployDirectory = null;
            return new DeployCheckResult(
                DeployCheckNames.ApplicationPackage,
                hasActions,
                hasActions ? "no runtime artifact configured; deploying an Actions-only App" : "runtime command is required");
        }
        try
        {
            resolvedDeployDirectory = ApplicationBuildExecutor.ResolveWorkspacePath(
                directory,
                string.IsNullOrWhiteSpace(deployDir) ? "." : deployDir,
                "runtime artifact directory");
            if (!buildOnRunner && !Directory.Exists(resolvedDeployDirectory))
                throw new DirectoryNotFoundException($"Runtime artifact directory does not exist: {resolvedDeployDirectory}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or DirectoryNotFoundException)
        {
            nextSteps.Add("Fix application.build.outputDirectory in automaton.json or pass --dir with a valid directory");
            resolvedDeployDirectory = null;
            return new DeployCheckResult(DeployCheckNames.ApplicationPackage, false, ex.Message);
        }

        var status = buildOnRunner ? "runtime artifact directory will be produced by the assigned Runner" : "runtime artifact directory ready";
        return new DeployCheckResult(DeployCheckNames.ApplicationPackage, true, $"{status}: {Path.GetRelativePath(directory, resolvedDeployDirectory)}");
    }

    private static ApplicationContract ResolveDeploymentApplication(ApplicationContract application, string? directoryOverride)
    {
        if (string.IsNullOrWhiteSpace(directoryOverride))
            return application;

        var copy = JsonSerializer.Deserialize<ApplicationContract>(JsonSerializer.Serialize(application))
            ?? throw new InvalidOperationException("The application contract could not be copied for deployment.");
        copy.Build.OutputDirectory = directoryOverride;
        ApplicationContractValidator.EnsureValid(copy);
        return copy;
    }

    private static DeployCheckResult ValidateActions(string directory, ISet<string> nextSteps, out int actionCount)
    {
        try
        {
            var actions = AppActionManifestParser.Discover(directory);
            foreach (var action in actions)
            {
                var entrypoint = Path.GetFullPath(Path.Combine(action.DirectoryPath, action.Manifest.Spec.Entrypoint));
                var actionRoot = Path.GetFullPath(action.DirectoryPath);
                if (!entrypoint.StartsWith(actionRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(entrypoint))
                    throw new InvalidOperationException($"Action '{action.Key}' entrypoint '{action.Manifest.Spec.Entrypoint}' does not exist.");
            }
            actionCount = actions.Count;
            return new DeployCheckResult("actions", true, actionCount == 0 ? "no Actions found" : $"validated {actionCount} Action module(s)");
        }
        catch (Exception ex)
        {
            actionCount = 0;
            nextSteps.Add("Fix Action manifests under actions/{action-key}/action.yaml and rerun automaton deploy");
            return new DeployCheckResult("actions", false, ex.Message);
        }
    }

    private static DeployResult CreateResult(
        bool success,
        IReadOnlyList<DeployCheckResult> checks,
        IEnumerable<string> nextSteps,
        Models.AutomatonConfig? projectConfig,
        string? appId,
        string? server,
        bool clean,
        string? deployDirectory,
        DeployUploadResult? uploadResult,
        DeployRuntimeResult? runtimeResult,
        DeploymentTargetInfo? target = null)
    {
        return new DeployResult
        {
            Success = success,
            Summary = success
                ? checks.Any(check => !check.Success && check.IsWarning) ? "Deploy complete with warnings" : "Deploy complete"
                : "Deploy failed",
            AppId = appId,
            AppName = projectConfig?.Name,
            TargetSlug = target?.Slug,
            TargetName = target?.Name,
            RunnerName = target?.Runner?.Name,
            DeploymentMode = target == null || target.IsDefault ? "default" : "named",
            OpenAppUrl = !string.IsNullOrWhiteSpace(appId) ? CliAppUrlBuilder.BuildStudioApplicationUrl(server, appId) : null,
            StudioAppUrl = !string.IsNullOrWhiteSpace(appId) ? CliAppUrlBuilder.BuildStudioAppUrl(server, appId) : null,
            DeployDirectory = deployDirectory,
            Clean = clean,
            Checks = checks,
            NextSteps = nextSteps.ToList(),
            RuntimeUploaded = uploadResult,
            Runtime = runtimeResult,
            HasWarnings = checks.Any(check => !check.Success && check.IsWarning),
            RuntimeRunning = runtimeResult?.Success == true && runtimeResult.RunnerCount > 0
        };
    }

    internal static System.Diagnostics.ProcessStartInfo CreateBuildProcessStartInfo(
        ApplicationCommandSpec command,
        string workingDir)
        => ApplicationBuildExecutor.CreateStartInfo(command, workingDir);

    private static async Task<(DeployUploadResult Upload, DeployRuntimeResult Deploy)> DeployPackageAsync(
        string server,
        string apiKey,
        string appId,
        string? targetSlug,
        Stream zip,
        bool clean,
        CancellationToken cancellationToken)
    {
        var sizeMb = zip.Length / 1_048_576.0;
        using var client = new AutomatonApiClient(server, apiKey);
        var deployResult = await client.DeployPackageAsync(
            appId, zip, clean, targetSlug, cancellationToken);
        var sourceFiles = deployResult.TryGetProperty("sourceFilesDeployed", out var sourceProperty) ? sourceProperty.GetInt32() : 0;
        var runtimeFiles = deployResult.TryGetProperty("runtimeFilesDeployed", out var runtimeProperty) ? runtimeProperty.GetInt32() : 0;
        var success = deployResult.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();
        var message = deployResult.TryGetProperty("message", out var messageProperty) ? messageProperty.GetString() : null;
        if (deployResult.TryGetProperty("actions", out var actionsProperty) && actionsProperty.ValueKind == JsonValueKind.Array)
        {
            var actionSummary = string.Join("; ", actionsProperty.EnumerateArray().Select(action =>
                $"Action {action.GetProperty("actionKey").GetString()}: {action.GetProperty("status").GetString()}"));
            if (!string.IsNullOrWhiteSpace(actionSummary)) message = string.IsNullOrWhiteSpace(message) ? actionSummary : $"{message} {actionSummary}";
        }
        var runnerCount = deployResult.TryGetProperty("runnerCount", out var runnerCountProperty) ? runnerCountProperty.GetInt32() : 0;
        var state = deployResult.TryGetProperty("state", out var stateProperty) ? stateProperty.GetString() : null;
        var requiresRetry = deployResult.TryGetProperty("requiresRetry", out var requiresRetryProperty) && requiresRetryProperty.GetBoolean();
        var logs = deployResult.TryGetProperty("logs", out var logsProperty)
            && logsProperty.ValueKind == JsonValueKind.Array
            ? logsProperty.EnumerateArray()
                .Where(log => log.ValueKind == JsonValueKind.String)
                .Select(log => log.GetString()!)
                .ToArray()
            : null;

        return (
            new DeployUploadResult(sourceFiles + runtimeFiles, 0, sizeMb),
            new DeployRuntimeResult(success, runnerCount, message, state, requiresRetry, logs));
    }

    private static async Task<JsonElement> EnsureManagedRuntimeReadyAsync(
        string server,
        string apiKey,
        string appId,
        CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.GetManagedRuntimeReadinessAsync(appId, cancellationToken);
    }

    private static void AddManagedRuntimeNextSteps(ISet<string> nextSteps, DeployRuntimeResult runtimeResult)
    {
        if (string.Equals(runtimeResult.Message, "No connected runner is assigned to this app.", StringComparison.Ordinal))
        {
            nextSteps.Add("This app is assigned to a runner, but that runner is not currently connected.");
            nextSteps.Add("Bring the assigned runner online or reassign the app to a healthy runner in Automaton Studio, then rerun automaton deploy.");
            return;
        }

        switch (runtimeResult.State)
        {
            case DeployStatePreparingManagedRuntime:
                nextSteps.Add("Managed runtime is being prepared. Rerun automaton deploy in a moment.");
                nextSteps.Add("Check Hosted Runners in Automaton Studio if provisioning takes longer than expected.");
                return;
            case DeployStateManagedRunnerWaitingForConnection:
                nextSteps.Add("Managed runner was provisioned but has not connected yet. Rerun automaton deploy in a moment.");
                nextSteps.Add("Check Hosted Runners in Automaton Studio for runner connection status.");
                return;
            case DeployStateManagedRunnerProvisionFailed:
                nextSteps.Add("Managed runner could not be provisioned. Check Hosted Runners in Automaton Studio.");
                nextSteps.Add("Inspect worker and hosted runner logs, then rerun automaton deploy.");
                return;
            case DeployStateManagedRuntimeUnavailable:
                nextSteps.Add("This workspace plan does not include managed application runtime.");
                nextSteps.Add("Assign a self-hosted runner or change the workspace plan, then rerun automaton deploy.");
                return;
            default:
                nextSteps.Add("Rerun automaton deploy with --runner <runner-id-or-name>");
                nextSteps.Add("If no connected runner exists, create or deploy one in Automaton Studio, then rerun automaton deploy");
                return;
        }
    }

    private static string FormatUploadMessage(string verb, string subject, DeployUploadResult result)
    {
        return $"{verb} {result.Written} {subject}, {result.Skipped} ignored ({result.SizeMb:F1} MB)";
    }

    private void ReportProgress(string message)
    {
        progress?.Invoke(message);
    }

    private void ReportDeploymentLogs(IReadOnlyList<string>? logs)
    {
        if (logs is null)
            return;

        foreach (var log in logs)
            ReportProgress(log);
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
