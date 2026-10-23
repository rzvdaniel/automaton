using System.Text.Json;
using Automaton.Cli.Models;
using Automaton.Core.Models;
using Automaton.Core.Services;

namespace Automaton.Cli.Services;

public sealed class DeployService
{
    private sealed record LocalDeployPreflight(
        Models.AutomatonConfig? ProjectConfig,
        string? Server,
        string? ApiKey,
        string? AppId,
        string? DeployDirectory,
        List<DeployCheckResult> Checks,
        HashSet<string> NextSteps,
        bool IsValid);

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
        var preflight = CreateLocalPreflight(directory, directoryOverride, environmentName);
        var checks = preflight.Checks;
        var nextSteps = preflight.NextSteps;
        DeployUploadResult? uploadResult = null;
        DeployRuntimeResult? runtimeResult = null;

        foreach (var check in checks)
        {
            checkReported?.Invoke(check);
        }

        var projectConfig = preflight.ProjectConfig;
        var server = preflight.Server;
        var apiKey = preflight.ApiKey;
        var appId = preflight.AppId;
        var deployDirectory = preflight.DeployDirectory;

        if (!preflight.IsValid)
        {
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, directoryOverride, uploadResult, runtimeResult, target);
        }

        var deploymentApplication = config.ResolveDeploymentApplication(projectConfig!.Application, directory, directoryOverride);

        try
        {
            ReportProgress("Creating restore point before deployment...");
            var snapshotResult = await createSnapshotAsync(server!, apiKey!, appId!, cancellationToken);
            AddCheck(checks, new DeployCheckResult("restore_point", snapshotResult.Created, snapshotResult.Message, IsWarning: !snapshotResult.Created));
            ReportProgress(snapshotResult.Message);
        }
        catch (Exception ex)
        {
            AddCheck(checks, new DeployCheckResult("restore_point", false, ex.Message, IsWarning: true));
            ReportProgress($"Restore point warning: {ex.Message}");
        }

        var readinessFailure = await CheckManagedRuntimeReadinessAsync(
            preflight, clean, target, uploadResult, cancellationToken);
        if (readinessFailure != null) return readinessFailure;

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
                server!, apiKey!, appId!, target?.IsDefault == true ? null : target?.Slug, zip, clean, cancellationToken);
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
            AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, false, runtimeResult.Message ?? "deploy package did not reach any runner"));
            return CreateResult(false, checks, nextSteps, projectConfig, appId, server, clean, deployDirectory, uploadResult, runtimeResult, target);
        }

        AddCheck(checks, new DeployCheckResult(DeployCheckNames.PackageDeploy, true, runtimeResult.Message ?? $"deploy package applied to {runtimeResult.RunnerCount} runner(s)"));
        ReportProgress(runtimeResult.Message ?? $"Deploy package applied to {runtimeResult.RunnerCount} runner(s).");

        return CreateResult(
            true, checks, nextSteps, projectConfig, appId, server, clean,
            deployDirectory, uploadResult, runtimeResult, target);
    }

    private async Task<DeployResult?> CheckManagedRuntimeReadinessAsync(
        LocalDeployPreflight preflight,
        bool clean,
        DeploymentTargetInfo? target,
        DeployUploadResult? uploadResult,
        CancellationToken cancellationToken)
    {
        // Every deployed app uses the configured generic runtime contract. The
        // runner assignment is not inferred from source files or technology.
        if (ensureManagedRuntimeReadyAsync == null || target?.IsDefault == false) return null;
        var readiness = await WaitForManagedRuntimeAsync(
            preflight.Server!, preflight.ApiKey!, preflight.AppId!, cancellationToken);
        if (readiness.Ready) return null;
        preflight.NextSteps.Add(readiness.NextStep);
        AddCheck(preflight.Checks, new DeployCheckResult(
            DeployCheckNames.RunnerAssignment, false, readiness.Message));
        AddReadinessNextSteps(preflight.NextSteps, readiness);
        var runtime = new DeployRuntimeResult(
            false, 0, readiness.Message, readiness.State, RequiresRetry: true);
        return CreateResult(
            false,
            preflight.Checks,
            preflight.NextSteps,
            preflight.ProjectConfig,
            preflight.AppId,
            preflight.Server,
            clean,
            preflight.DeployDirectory,
            uploadResult,
            runtime,
            target);
    }

    private static void AddReadinessNextSteps(
        ISet<string> nextSteps,
        (bool Ready, string Message, string NextStep, string State) readiness)
    {
        if (!IsManagedRuntimeFailureState(readiness.State)) return;
        AddManagedRuntimeNextSteps(
            nextSteps,
            new DeployRuntimeResult(false, 0, readiness.Message, readiness.State, RequiresRetry: true));
    }

    private static bool IsManagedRuntimeFailureState(string state)
        => state is DeployStateManagedRunnerProvisionFailed
            or DeployStateManagedRunnerWaitingForConnection
            or DeployStateManagedRuntimeUnavailable
            or DeployStatePreparingManagedRuntime;

    public DeployResult? ValidateLocalProject(
        string directory,
        bool clean,
        string? directoryOverride,
        string? environmentName = null)
    {
        var preflight = CreateLocalPreflight(directory, directoryOverride, environmentName);
        return preflight.IsValid
            ? null
            : CreateResult(
                false,
                preflight.Checks,
                preflight.NextSteps,
                preflight.ProjectConfig,
                preflight.AppId,
                preflight.Server,
                clean,
                directoryOverride,
                uploadResult: null,
                runtimeResult: null);
    }

    private LocalDeployPreflight CreateLocalPreflight(
        string directory,
        string? directoryOverride,
        string? environmentName)
    {
        var checks = new List<DeployCheckResult>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configPath = Path.Combine(directory, AutomatonConfigFileName);
        var projectConfig = config.TryLoadProjectConfig(directory);
        var resolved = config.ResolveContext(projectConfig, directory, environmentName);
        var server = resolved.Server;
        var apiKey = resolved.ApiKey;
        var appId = resolved.AppId;

        var appLinkCheck = ValidateAppLink(File.Exists(configPath), projectConfig, appId, nextSteps);
        checks.Add(appLinkCheck);
        var credentialsCheck = ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(credentialsCheck);
        if (!appLinkCheck.Success
            || !credentialsCheck.Success
            || projectConfig is null
            || string.IsNullOrWhiteSpace(server)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            return new LocalDeployPreflight(projectConfig, server, apiKey, appId, directoryOverride, checks, nextSteps, false);
        }

        var deploymentApplication = projectConfig.Application;
        try
        {
            deploymentApplication = config.ResolveDeploymentApplication(projectConfig.Application, directory, directoryOverride);
        }
        catch (InvalidOperationException exception)
        {
            checks.Add(new DeployCheckResult(ValidationCheckNames.ApplicationContract, false, exception.Message));
            return new LocalDeployPreflight(projectConfig, server, apiKey, appId, directoryOverride, checks, nextSteps, false);
        }
        var buildCheck = ValidateBuildForRunner(deploymentApplication.Build, directory, nextSteps);
        checks.Add(buildCheck);
        if (!buildCheck.Success)
        {
            return new LocalDeployPreflight(projectConfig, server, apiKey, appId, directoryOverride, checks, nextSteps, false);
        }

        var actionsCheck = DeployActionValidator.Validate(directory, nextSteps, out var actionCount);
        checks.Add(actionsCheck);
        if (!actionsCheck.Success)
        {
            return new LocalDeployPreflight(projectConfig, server, apiKey, appId, directoryOverride, checks, nextSteps, false);
        }

        var applicationCheck = ValidateApplicationPackage(
            directory,
            ApplicationBuildArtifactSelector.GetRelativeDirectory(deploymentApplication),
            actionCount > 0,
            deploymentApplication.Runtime.Command is not null || deploymentApplication.Compose is not null,
            deploymentApplication.Build.Command is not null,
            nextSteps,
            out var deployDirectory);
        if (!applicationCheck.Success && deploymentApplication.Runtime.Command is null
            && deploymentApplication.Compose is null && actionCount == 0)
        {
            nextSteps.Add("Add application.runtime.command.executable to automaton.json, or define at least one Action for an Actions-only app");
        }
        checks.Add(applicationCheck);
        if (!applicationCheck.Success)
        {
            return new LocalDeployPreflight(projectConfig, server, apiKey, appId, deployDirectory, checks, nextSteps, false);
        }

        return new LocalDeployPreflight(projectConfig, server, apiKey, appId, deployDirectory, checks, nextSteps, true);
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
            Summary = BuildSummary(success, checks),
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

    private static string BuildSummary(bool success, IEnumerable<DeployCheckResult> checks)
    {
        if (!success) return "Deploy failed";
        return checks.Any(check => !check.Success && check.IsWarning)
            ? "Deploy complete with warnings"
            : "Deploy complete";
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
        message = AppendActionSummary(deployResult, message);
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

    private static string? AppendActionSummary(JsonElement deployResult, string? message)
    {
        if (!deployResult.TryGetProperty("actions", out var actions) ||
            actions.ValueKind != JsonValueKind.Array)
        {
            return message;
        }
        var actionSummary = string.Join("; ", actions.EnumerateArray().Select(action =>
            $"Action {action.GetProperty("actionKey").GetString()}: {action.GetProperty("status").GetString()}"));
        if (string.IsNullOrWhiteSpace(actionSummary)) return message;
        return string.IsNullOrWhiteSpace(message) ? actionSummary : $"{message} {actionSummary}";
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
        var message = runtimeResult.Message;
        if (string.Equals(runtimeResult.Message, "No connected runner is assigned to this app.", StringComparison.Ordinal))
        {
            nextSteps.Add("This app is assigned to a runner, but that runner is not currently connected.");
            nextSteps.Add("Bring the assigned runner online or reassign the app to a healthy runner in Automaton Studio, then rerun automaton deploy.");
            return;
        }

        if (ContainsAny(message,
                "must be upgraded for container-based application execution",
                "requires an upgrade for the current Generic Runtime deployment protocol"))
        {
            nextSteps.Add("The assigned Runner does not support the current container-based application deployment protocol.");
            nextSteps.Add("Ask an administrator to upgrade the Hosted Runner, or assign a compatible Runner, then rerun automaton deploy.");
            return;
        }

        if (ContainsAny(message,
                "Your subscription plan does not include a hosted runner",
                "hosted runner configuration for your subscription plan",
                "hosted runner specification configured for your subscription plan"))
        {
            nextSteps.Add("The active subscription plan does not have a usable Hosted Runner capability or specification.");
            nextSteps.Add("Ask an administrator to enable Hosted Runner and map an active specification to the plan, then rerun automaton deploy.");
            return;
        }

        if (ContainsAny(message,
                "No active runner host has enough available capacity",
                "selected Runner Host has no Infrastructure Runner assigned",
                "Normal hosted Runner provisioning requires an Infrastructure Runner transport"))
        {
            nextSteps.Add("No eligible Runner Host is available for managed Hosted Runner provisioning.");
            nextSteps.Add("An administrator should check that a non-production Runner Host is online, validated, approved, has capacity, and has an online protocol-4 Infrastructure Runner with HostContainerExecution.");
            return;
        }

        if (ContainsAny(message, "requires an immutable container image reference"))
        {
            nextSteps.Add("Hosted Runner provisioning requires an immutable repository@sha256 image reference.");
            nextSteps.Add("Ask an administrator to configure the Hosted Runner image digest, then retry provisioning.");
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
                nextSteps.Add("Managed runtime is unavailable for the assigned Runner.");
                nextSteps.Add("Check the Runner connection and container-execution compatibility in Automaton Studio, then rerun automaton deploy.");
                return;
            default:
                nextSteps.Add("Inspect the deployment failure and Runner logs in Automaton Studio, correct the app or runtime configuration, then rerun automaton deploy.");
                return;
        }
    }

    private static bool ContainsAny(string? value, params string[] fragments)
    {
        return value != null && fragments.Any(fragment =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
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
