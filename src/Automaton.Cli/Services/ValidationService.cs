using Automaton.Cli.Models;
using Automaton.Core.Models;

namespace Automaton.Cli.Services;

public sealed class ValidationService
{
    private const string AutomatonConfigFileName = "automaton.json";
    private const string MinimalConfigExample = """
{
  "name": "My App",
  "application": {
    "build": {
      "command": {
        "executable": "build-tool",
        "arguments": [],
        "workingDirectory": "."
      }
    },
    "runtime": {
      "command": {
        "executable": "app-server",
        "arguments": [],
        "workingDirectory": "."
      },
      "port": 8080
    },
    "health": {},
    "storage": {
      "dataDirectory": "data"
    }
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
        var resolved = _config.ResolveContext(projectConfig, directory);
        var resolvedServer = resolved.Server;
        var resolvedApiKey = resolved.ApiKey;
        var resolvedAppId = resolved.AppId;

        var appLinkCheck = ValidateAppLink(configFileExists, projectConfig, resolvedAppId, resolvedServer, nextSteps);
        checks.Add(appLinkCheck);

        checks.Add(ValidateApplicationContract(projectConfig, directory));

        checks.AddRange(CollectWarnings(directory, projectConfig, strict, nextSteps));

        var remoteAppCheck = await ValidateRemoteAppAccessAsync(
            appLinkCheck,
            projectConfig,
            resolvedAppId,
            resolvedServer,
            resolvedApiKey,
            nextSteps,
            cancellationToken);

        checks.Add(remoteAppCheck.Check);

        if (remoteAppCheck.Check.Success
            && await SecretStatusCheck.FindMissingAsync(resolvedServer!, resolvedApiKey!, resolvedAppId!,
                projectConfig?.Application.Secrets, cancellationToken) is { } missingSecrets)
        {
            nextSteps.Add("Run automaton secrets status and fill missing secrets with automaton secrets set");
            checks.Add(CreateWarningOrFailure(SecretStatusCheck.CheckName, missingSecrets, strict));
        }

        if (remoteAppCheck.Check.Success
            && await PermissionStatusCheck.FindWaitingAsync(resolvedServer!, resolvedApiKey!, resolvedAppId!,
                projectConfig?.Application.Permissions, cancellationToken) is { } waitingPermissions)
        {
            // Approval is a person's decision, so this stays a warning even with --strict.
            nextSteps.Add("Ask a system administrator to approve the app's permissions (automaton permissions status shows the link)");
            checks.Add(new ValidationCheckResult(PermissionStatusCheck.CheckName, true, waitingPermissions, IsWarning: true));
        }

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
        string? resolvedAppId,
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

        if (string.IsNullOrWhiteSpace(resolvedAppId))
        {
            nextSteps.Add("Run automaton create or automaton link <app-id> with --env <name> to link a remote app");
            nextSteps.Add($"Minimal automaton.json example:{Environment.NewLine}{MinimalConfigExample}");
            return new ValidationCheckResult(ValidationCheckNames.AppLink, false, "the selected environment is not linked to an appId");
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
            $"linked app {resolvedAppId} on {resolvedServer}");
    }

    private ValidationCheckResult ValidateApplicationContract(
        Models.AutomatonConfig? projectConfig,
        string projectDirectory)
    {
        if (projectConfig is null)
        {
            return new ValidationCheckResult(
                ValidationCheckNames.ApplicationContract,
                false,
                "application contract is unavailable because automaton.json could not be loaded");
        }

        IReadOnlyList<string> errors;
        try
        {
            _config.LoadComposeConfiguration(projectConfig.Application, projectDirectory);
            errors = ApplicationContractValidator.Validate(projectConfig.Application);
        }
        catch (InvalidOperationException exception)
        {
            errors = [exception.Message];
        }
        if (errors.Count > 0)
        {
            return new ValidationCheckResult(
                ValidationCheckNames.ApplicationContract,
                false,
                string.Join("; ", errors));
        }

        return new ValidationCheckResult(
            ValidationCheckNames.ApplicationContract,
            true,
            "application contract is valid");
    }

    private async Task<RemoteAppCheckResult> ValidateRemoteAppAccessAsync(
        ValidationCheckResult appLinkCheck,
        Models.AutomatonConfig? projectConfig,
        string? resolvedAppId,
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
            var remoteApp = await _loadRemoteAppAsync(resolvedServer!, resolvedApiKey, resolvedAppId!, cancellationToken);
            return new RemoteAppCheckResult(
                new ValidationCheckResult(ValidationCheckNames.RemoteAppAccess, true, $"remote app found: {resolvedAppId}"),
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
        var targets = await client.ListDeploymentTargetsAsync(appId, cancellationToken);
        return new RemoteAppValidationInfo(
            targets.SingleOrDefault(target => target.IsDefault)?.Runner?.Id);
    }

    private sealed record RemoteAppCheckResult(ValidationCheckResult Check, RemoteAppValidationInfo? RemoteApp);
}
