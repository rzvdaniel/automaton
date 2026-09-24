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
        var request = ValidateRequest(directory, appId, server, apiKey, environmentName, checks, nextSteps);
        if (request is null) return FailedResult(checks, nextSteps, server);

        try
        {
            return await CompleteLinkAsync(request, checks, nextSteps, cancellationToken);
        }
        catch (Exception ex)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, ex.Message));
            nextSteps.Add("Confirm the app ID or exact slug and selected profile, then rerun automaton link");
            return FailedResult(checks, nextSteps, server);
        }
    }

    private LinkRequest? ValidateRequest(
        string directory,
        string? appId,
        string? server,
        string? apiKey,
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(credentialsCheck);
        if (!credentialsCheck.Success || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey)) return null;
        if (string.IsNullOrWhiteSpace(appId))
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, "app ID is required"));
            nextSteps.Add("Pass an app ID or answer the app ID prompt");
            return null;
        }

        var environment = _config.ResolveAppBindingEnvironmentName(directory, environmentName);
        var projectRoot = _config.FindNearestProjectRoot(directory);
        var configPath = Path.Combine(projectRoot ?? directory, "automaton.json");
        var projectConfig = projectRoot is null ? null : _config.TryLoadProjectConfig(projectRoot);
        if (projectConfig is null)
        {
            var detail = File.Exists(configPath)
                ? "automaton.json is invalid or unreadable"
                : "automaton.json is required to link existing local source";
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, detail));
            nextSteps.Add("Create or repair a valid local application contract before running automaton link");
            return null;
        }

        return new LinkRequest(directory, appId.Trim(), server, apiKey, environment, configPath, projectConfig);
    }

    private async Task<LinkAppResult> CompleteLinkAsync(
        LinkRequest request,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps,
        CancellationToken cancellationToken)
    {
        var app = await _getAppAsync(request.Server, request.ApiKey, request.AppId, cancellationToken);
        var (canonicalAppId, appName) = ReadAppIdentity(app);
        var environment = _config.LoadNamedEnvironmentConfig(request.Directory, request.EnvironmentName) ?? new GlobalConfig();
        EnsureCanLink(environment.AppId ?? request.ProjectConfig.AppId, canonicalAppId);
        SaveLink(request, environment, canonicalAppId);
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, true,
            $"linked existing local source to app {canonicalAppId}; source files were not changed"));
        nextSteps.Add("Run automaton deploy to deploy this local source");
        return new LinkAppResult
        {
            Success = true,
            Summary = "Link complete",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            AppId = canonicalAppId,
            Name = appName,
            Server = request.Server,
            ConfigPath = request.ConfigPath
        };
    }

    private static (string Id, string Name) ReadAppIdentity(JsonElement app)
    {
        var id = app.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String
            ? idProperty.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("The app response did not include an app ID.");
        var name = app.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()!
            : id;
        return (id, name);
    }

    private static void EnsureCanLink(string? existingAppId, string canonicalAppId)
    {
        if (string.IsNullOrWhiteSpace(existingAppId)
            || string.Equals(existingAppId, canonicalAppId, StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException(
            $"This project profile is already linked to app {existingAppId}. Select another profile with --env <name> to link a different app.");
    }

    private void SaveLink(LinkRequest request, GlobalConfig environment, string appId)
    {
        environment.Server = request.Server;
        environment.AppId = appId;
        _config.SaveNamedEnvironmentConfig(request.Directory, request.EnvironmentName, environment);
        _config.SaveProjectEnvironmentSelection(request.Directory,
            new ProjectEnvironmentSelection { Default = request.EnvironmentName });
    }

    private static LinkAppResult FailedResult(
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        string? server) => new()
        {
            Success = false,
            Summary = "Link failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            Server = server
        };

    private sealed record LinkRequest(
        string Directory,
        string AppId,
        string Server,
        string ApiKey,
        string EnvironmentName,
        string ConfigPath,
        AutomatonConfig ProjectConfig);

    private static async Task<JsonElement> GetAppAsync(string server, string apiKey, string appId, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.GetAppByIdOrSlugAsync(appId, cancellationToken);
    }
}
