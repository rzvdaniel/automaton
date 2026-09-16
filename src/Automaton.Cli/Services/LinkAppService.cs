using Automaton.Cli.Models;
using System.Text.Json;

namespace Automaton.Cli.Services;

public sealed class LinkAppService
{
    private readonly ConfigService _config;
    private readonly Func<string, string, string, CancellationToken, Task<JsonElement>> _getAppAsync;

    public LinkAppService(
        ConfigService config,
        Func<string, string, string, CancellationToken, Task<JsonElement>>? getAppAsync = null)
    {
        _config = config;
        _getAppAsync = getAppAsync ?? GetAppAsync;
    }

    public async Task<LinkAppResult> LinkAsync(
        string directory,
        string? appId,
        string? server,
        string? apiKey,
        string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedEnvironmentName = _config.ResolveAppBindingEnvironmentName(directory, environmentName);

        var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(credentialsCheck);

        if (!credentialsCheck.Success || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            return new LinkAppResult
            {
                Success = false,
                Summary = "Link failed",
                Checks = checks,
                NextSteps = nextSteps.ToList()
            };
        }

        if (string.IsNullOrWhiteSpace(appId))
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, "app ID is required"));
            nextSteps.Add("Pass an app ID or answer the app ID prompt");
            return new LinkAppResult
            {
                Success = false,
                Summary = "Link failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = server
            };
        }

        var projectRoot = _config.FindNearestProjectRoot(directory);
        var projectConfigPath = projectRoot is null ? Path.Combine(directory, "automaton.json") : Path.Combine(projectRoot, "automaton.json");
        var projectConfig = projectRoot is null ? null : _config.TryLoadProjectConfig(projectRoot);
        if (projectRoot is null || projectConfig is null)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.LinkApp,
                false,
                !File.Exists(projectConfigPath)
                    ? "automaton.json is required to link existing local source"
                    : "automaton.json is invalid or unreadable"));
            nextSteps.Add("Create or repair a valid local application contract before running automaton link");
            return new LinkAppResult
            {
                Success = false,
                Summary = "Link failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = server
            };
        }

        try
        {
            var app = await _getAppAsync(server, apiKey, appId.Trim(), cancellationToken);
            var canonicalAppId = app.TryGetProperty("id", out var appIdProperty)
                && appIdProperty.ValueKind == JsonValueKind.String
                ? appIdProperty.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(canonicalAppId))
            {
                throw new InvalidOperationException("The app response did not include an app ID.");
            }
            var appName = app.TryGetProperty("name", out var nameProperty)
                && nameProperty.ValueKind == JsonValueKind.String
                ? nameProperty.GetString()!
                : canonicalAppId;

            var namedEnvironment = _config.LoadNamedEnvironmentConfig(directory, resolvedEnvironmentName) ?? new GlobalConfig();
            var existingAppId = namedEnvironment.AppId ?? projectConfig.AppId;
            if (!string.IsNullOrWhiteSpace(existingAppId)
                && !string.Equals(existingAppId, canonicalAppId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"This project profile is already linked to app {existingAppId}. Select another profile with --env <name> to link a different app.");
            }

            namedEnvironment.Server = server;
            namedEnvironment.AppId = canonicalAppId;
            _config.SaveNamedEnvironmentConfig(directory, resolvedEnvironmentName, namedEnvironment);
            _config.SaveProjectEnvironmentSelection(directory, new ProjectEnvironmentSelection { Default = resolvedEnvironmentName });

            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.LinkApp,
                true,
                $"linked existing local source to app {canonicalAppId}; source files were not changed"));
            nextSteps.Add("Run automaton deploy to deploy this local source");

            return new LinkAppResult
            {
                Success = true,
                Summary = "Link complete",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                AppId = canonicalAppId,
                Name = appName,
                Server = server,
                ConfigPath = projectConfigPath
            };
        }
        catch (Exception ex)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, ex.Message));
            nextSteps.Add("Confirm the app ID or exact slug and selected profile, then rerun automaton link");
            return new LinkAppResult
            {
                Success = false,
                Summary = "Link failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = server
            };
        }
    }

    private static async Task<JsonElement> GetAppAsync(string server, string apiKey, string appId, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.GetAppByIdOrSlugAsync(appId, cancellationToken);
    }
}
