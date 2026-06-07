using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public sealed class ValidationService
{
    private const string AutomatonConfigFileName = "automaton.json";
    private const string MinimalConfigExample = """
{
  "name": "My App",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
""";

    private readonly ConfigService _config;
    private readonly Func<string, string, string, CancellationToken, Task<RemoteAppValidationInfo>> _loadRemoteAppAsync;

    public ValidationService(
        ConfigService config,
        Func<string, string, string, CancellationToken, Task<RemoteAppValidationInfo>>? loadRemoteAppAsync = null)
    {
        _config = config;
        _loadRemoteAppAsync = loadRemoteAppAsync ?? LoadRemoteAppAsync;
    }

    public async Task<ValidationResult> ValidateAsync(string directory, bool strict = false, CancellationToken cancellationToken = default)
    {
        var checks = new List<ValidationCheckResult>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var configPath = Path.Combine(directory, AutomatonConfigFileName);
        var configFileExists = File.Exists(configPath);
        var projectConfig = _config.TryLoadProjectConfig(directory);
        var (resolvedServer, resolvedApiKey) = _config.ResolveCredentials(projectConfig, directory);

        var appLinkCheck = ValidateAppLink(configFileExists, projectConfig, resolvedServer, nextSteps);
        checks.Add(appLinkCheck);

        var frontendCheck = ValidateFrontendDeployShape(directory, projectConfig);
        checks.Add(frontendCheck);

        var backendCheck = ValidateBackendDeployShape(directory, projectConfig, out var backendControllers);
        checks.Add(backendCheck);

        checks.AddRange(CollectWarnings(directory, projectConfig, strict, nextSteps));

        var remoteAppCheck = await ValidateRemoteAppAccessAsync(
            appLinkCheck,
            projectConfig,
            resolvedServer,
            resolvedApiKey,
            nextSteps,
            cancellationToken);

        checks.Add(ValidateBackendRunner(backendControllers, remoteAppCheck, nextSteps));
        checks.Add(remoteAppCheck.Check);

        var success = checks.All(check => check.Success);

        return new ValidationResult
        {
            Success = success,
            Summary = success ? "Validation passed" : "Validation failed",
            Checks = checks,
            NextSteps = nextSteps.ToList()
        };
    }

    private static IEnumerable<ValidationCheckResult> CollectWarnings(
        string directory,
        Models.AutomatonConfig? projectConfig,
        bool strict,
        ISet<string> nextSteps)
    {
        if (!File.Exists(Path.Combine(directory, ".automatonignore")))
        {
            nextSteps.Add("Create .automatonignore to keep local-only files out of deploy uploads");
            yield return CreateWarningOrFailure(
                "automatonIgnore",
                ".automatonignore is missing; deploy will still work, but local-only files may be uploaded unless they match default excludes",
                strict);
        }

        if (projectConfig != null && string.IsNullOrWhiteSpace(projectConfig.Name))
        {
            nextSteps.Add("Add a name to automaton.json for clearer status and publish output");
            yield return CreateWarningOrFailure(
                "appName",
                "automaton.json is missing a friendly app name",
                strict);
        }
    }

    private static ValidationCheckResult CreateWarningOrFailure(string name, string message, bool strict)
    {
        return strict
            ? new ValidationCheckResult(name, false, $"strict: {message}")
            : new ValidationCheckResult(name, true, message, IsWarning: true);
    }

    private static ValidationCheckResult ValidateAppLink(
        bool configFileExists,
        Models.AutomatonConfig? projectConfig,
        string? resolvedServer,
        ISet<string> nextSteps)
    {
        if (!configFileExists)
        {
            nextSteps.Add("Run automaton new \"My App\" or automaton link <app-id>");
            nextSteps.Add($"Minimal automaton.json example:{Environment.NewLine}{MinimalConfigExample}");
            return new ValidationCheckResult(ValidationCheckNames.AppLink, false, "automaton.json not found in the project root");
        }

        if (projectConfig == null)
        {
            nextSteps.Add("Fix the automaton.json file so it contains valid JSON");
            return new ValidationCheckResult(ValidationCheckNames.AppLink, false, "automaton.json is invalid or unreadable");
        }

        if (string.IsNullOrWhiteSpace(projectConfig.AppId))
        {
            nextSteps.Add("Run automaton create to create and link a remote app, or automaton link <app-id> to link an existing app");
            nextSteps.Add($"Minimal automaton.json example:{Environment.NewLine}{MinimalConfigExample}");
            return new ValidationCheckResult(ValidationCheckNames.AppLink, false, "automaton.json is missing appId; local scaffold exists but is not linked yet");
        }

        if (string.IsNullOrWhiteSpace(resolvedServer))
        {
            nextSteps.Add("Run automaton login to configure the hosted server and credentials");
            return new ValidationCheckResult(
                ValidationCheckNames.AppLink,
                false,
                "server could not be resolved from environment, automaton.json, or CLI config");
        }

        return new ValidationCheckResult(
            ValidationCheckNames.AppLink,
            true,
            $"linked app {projectConfig.AppId} on {resolvedServer}");
    }

    private static ValidationCheckResult ValidateFrontendDeployShape(string directory, Models.AutomatonConfig? projectConfig)
    {
        if (projectConfig?.Frontend == null)
        {
            return new ValidationCheckResult(
                ValidationCheckNames.FrontendDeployShape,
                false,
                "frontend configuration is missing from automaton.json");
        }

        if (projectConfig.Frontend.Build is not null && string.IsNullOrWhiteSpace(projectConfig.Frontend.Build))
        {
            return new ValidationCheckResult(
                ValidationCheckNames.FrontendDeployShape,
                false,
                "frontend.build is configured in automaton.json but is empty");
        }

        string deployDirectory;
        try
        {
            deployDirectory = ZipService.ResolveDeployDirectory(directory, projectConfig.Frontend.DeployDir);
        }
        catch (Exception ex)
        {
            return new ValidationCheckResult(ValidationCheckNames.FrontendDeployShape, false, ex.Message);
        }

        if (!Directory.Exists(deployDirectory))
        {
            return new ValidationCheckResult(
                ValidationCheckNames.FrontendDeployShape,
                false,
                $"deploy directory does not exist: {deployDirectory}");
        }

        var indexPath = Path.Combine(deployDirectory, "index.html");
        if (!File.Exists(indexPath))
        {
            return new ValidationCheckResult(
                ValidationCheckNames.FrontendDeployShape,
                false,
                $"no index.html found in deploy directory: {Path.GetRelativePath(directory, deployDirectory)}");
        }

        return new ValidationCheckResult(
            ValidationCheckNames.FrontendDeployShape,
            true,
            $"deploy directory ready: {Path.GetRelativePath(directory, deployDirectory)}");
    }

    private static ValidationCheckResult ValidateBackendDeployShape(
        string directory,
        Models.AutomatonConfig? projectConfig,
        out int backendControllers)
    {
        backendControllers = 0;
        var conventionalBackendDirectory = Path.Combine(directory, "backend");
        var conventionalBackendControllers = Directory.Exists(conventionalBackendDirectory)
            ? Directory.GetFiles(conventionalBackendDirectory, "*.py", SearchOption.AllDirectories).Length
            : 0;

        var backendDeployDir = projectConfig?.Backend?.DeployDir;
        if (string.IsNullOrWhiteSpace(backendDeployDir))
        {
            if (conventionalBackendControllers > 0)
            {
                return new ValidationCheckResult(
                    ValidationCheckNames.BackendDeployShape,
                    false,
                    "backend controllers were found, but backend.deployDir is missing from automaton.json");
            }

            return new ValidationCheckResult(
                ValidationCheckNames.BackendDeployShape,
                true,
                "no backend deploy configured");
        }

        string backendDirectory;
        try
        {
            backendDirectory = ZipService.ResolveDeployDirectory(directory, backendDeployDir);
        }
        catch (Exception ex)
        {
            return new ValidationCheckResult(ValidationCheckNames.BackendDeployShape, false, ex.Message);
        }

        backendControllers = Directory.GetFiles(backendDirectory, "*.py", SearchOption.AllDirectories).Length;
        return new ValidationCheckResult(
            ValidationCheckNames.BackendDeployShape,
            true,
            $"backend directory ready: {Path.GetRelativePath(directory, backendDirectory)}");
    }

    private static ValidationCheckResult ValidateBackendRunner(
        int backendControllers,
        RemoteAppCheckResult remoteAppCheck,
        ISet<string> nextSteps)
    {
        if (backendControllers == 0)
        {
            return new ValidationCheckResult(
                ValidationCheckNames.BackendRunner,
                true,
                "runner not required because no backend controllers were found");
        }

        if (!remoteAppCheck.Check.Success)
        {
            return new ValidationCheckResult(
                ValidationCheckNames.BackendRunner,
                false,
                "cannot verify runner assignment because remote app access failed");
        }

        if (string.IsNullOrWhiteSpace(remoteAppCheck.RemoteApp?.ExecutionRunnerId))
        {
            nextSteps.Add("Run automaton deploy and choose a connected runner when prompted");
            nextSteps.Add("If no connected runner exists, create or deploy one in Automaton Studio, then rerun automaton deploy");
            return new ValidationCheckResult(
                ValidationCheckNames.BackendRunner,
                false,
                $"{backendControllers} backend controller(s) found but no runner is assigned");
        }

        return new ValidationCheckResult(
            ValidationCheckNames.BackendRunner,
            true,
            $"runner assigned: {remoteAppCheck.RemoteApp.ExecutionRunnerId}");
    }

    private async Task<RemoteAppCheckResult> ValidateRemoteAppAccessAsync(
        ValidationCheckResult appLinkCheck,
        Models.AutomatonConfig? projectConfig,
        string? resolvedServer,
        string? resolvedApiKey,
        ISet<string> nextSteps,
        CancellationToken cancellationToken)
    {
        if (!appLinkCheck.Success)
        {
            return new RemoteAppCheckResult(
                new ValidationCheckResult(ValidationCheckNames.RemoteAppAccess, false, "skipped because app linkage failed"),
                null);
        }

        if (projectConfig == null)
        {
            return new RemoteAppCheckResult(
                new ValidationCheckResult(ValidationCheckNames.RemoteAppAccess, false, "skipped because automaton.json could not be loaded"),
                null);
        }

        if (string.IsNullOrWhiteSpace(resolvedApiKey))
        {
            nextSteps.Add("Run automaton login");
            return new RemoteAppCheckResult(
                new ValidationCheckResult(
                    ValidationCheckNames.RemoteAppAccess,
                    false,
                    "API key could not be resolved from environment or CLI config"),
                null);
        }

        try
        {
            var remoteApp = await _loadRemoteAppAsync(resolvedServer!, resolvedApiKey, projectConfig.AppId, cancellationToken);
            return new RemoteAppCheckResult(
                new ValidationCheckResult(ValidationCheckNames.RemoteAppAccess, true, $"remote app found: {projectConfig.AppId}"),
                remoteApp);
        }
        catch (Exception ex)
        {
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            return new RemoteAppCheckResult(
                new ValidationCheckResult(ValidationCheckNames.RemoteAppAccess, false, ex.Message),
                null);
        }
    }

    private static async Task<RemoteAppValidationInfo> LoadRemoteAppAsync(
        string server,
        string apiKey,
        string appId,
        CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        var app = await client.GetAppAsync(appId, cancellationToken);
        var executionRunnerId = app.TryGetProperty("executionRunnerId", out var runnerProperty)
            && runnerProperty.ValueKind == System.Text.Json.JsonValueKind.String
                ? runnerProperty.GetString()
                : null;

        return new RemoteAppValidationInfo(executionRunnerId);
    }

    private sealed record RemoteAppCheckResult(ValidationCheckResult Check, RemoteAppValidationInfo? RemoteApp);
}
