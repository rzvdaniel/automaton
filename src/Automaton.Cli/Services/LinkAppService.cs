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
        var validationFailure = ValidateRequest(
            directory, appId, server, apiKey, checks, nextSteps);
        if (validationFailure != null) return validationFailure;
        var context = CreateContext(directory, appId!, server!, apiKey!, resolvedEnvironmentName, checks, nextSteps);
        try
        {
            return await LinkExistingAppAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            return CreateFailure(context, ex.Message);
        }
    }

    private LinkAppResult? ValidateRequest(
        string directory,
        string? appId,
        string? server,
        string? apiKey,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var credentialFailure = ValidateCredentials(server, apiKey, checks, nextSteps);
        if (credentialFailure != null) return credentialFailure;
        if (ValidateAppId(appId, server, checks, nextSteps) is { } appIdFailure) return appIdFailure;
        return ValidateProjectContract(directory, server, checks, nextSteps);
    }

    private static LinkAppResult? ValidateCredentials(
        string? server,
        string? apiKey,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var check = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(check);
        return check.Success && !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(apiKey)
            ? null
            : Failure(checks, nextSteps);
    }

    private static LinkAppResult? ValidateAppId(
        string? appId,
        string? server,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        if (!string.IsNullOrWhiteSpace(appId)) return null;
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, "app ID is required"));
        nextSteps.Add("Pass an app ID or answer the app ID prompt");
        return Failure(checks, nextSteps, server);
    }

    private LinkAppResult? ValidateProjectContract(
        string directory,
        string? server,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var root = _config.FindNearestProjectRoot(directory);
        var path = Path.Combine(root ?? directory, "automaton.json");
        var projectConfig = root == null ? null : _config.TryLoadProjectConfig(root);
        if (root != null && projectConfig != null) return null;
        var message = !File.Exists(path)
            ? "automaton.json is required to link existing local source"
            : "automaton.json is invalid or unreadable";
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, message));
        nextSteps.Add("Create or repair a valid local application contract before running automaton link");
        return Failure(checks, nextSteps, server);
    }

    private LinkContext CreateContext(
        string directory,
        string appId,
        string server,
        string apiKey,
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var root = _config.FindNearestProjectRoot(directory)!;
        var projectConfig = _config.TryLoadProjectConfig(root)!;
        var environment = _config.LoadNamedEnvironmentConfig(directory, environmentName) ?? new GlobalConfig();
        return new LinkContext(directory, appId, server, apiKey, environmentName,
            Path.Combine(root, "automaton.json"), projectConfig, environment, checks, nextSteps);
    }

    private async Task<LinkAppResult> LinkExistingAppAsync(LinkContext context, CancellationToken cancellationToken)
    {
        var response = await _getAppAsync(
            context.Server, context.ApiKey, context.AppId.Trim(), cancellationToken);
        var identity = ReadAppIdentity(response);
        EnsureProfileCanLink(context, identity.Id);
        SaveProjectLink(context, identity.Id);
        context.Checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.LinkApp,
            true,
            $"linked existing local source to app {identity.Id}; source files were not changed"));
        context.NextSteps.Add("Run automaton deploy to deploy this local source");
        return new LinkAppResult
        {
            Success = true,
            Summary = "Link complete",
            Checks = context.Checks.ToList(),
            NextSteps = context.NextSteps.ToList(),
            AppId = identity.Id,
            Name = identity.Name,
            Server = context.Server,
            ConfigPath = context.ProjectConfigPath
        };
    }

    private static AppIdentity ReadAppIdentity(JsonElement app)
    {
        var id = app.TryGetProperty("id", out var appIdProperty) &&
                 appIdProperty.ValueKind == JsonValueKind.String
            ? appIdProperty.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("The app response did not include an app ID.");
        }
        var name = app.TryGetProperty("name", out var nameProperty) &&
                   nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()!
            : id;
        return new AppIdentity(id, name);
    }

    private static void EnsureProfileCanLink(LinkContext context, string canonicalAppId)
    {
        var existingAppId = context.Environment.AppId ?? context.ProjectConfig.AppId;
        if (!string.IsNullOrWhiteSpace(existingAppId) &&
            !string.Equals(existingAppId, canonicalAppId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This project profile is already linked to app {existingAppId}. Select another profile with --env <name> to link a different app.");
        }
    }

    private void SaveProjectLink(LinkContext context, string appId)
    {
        context.Environment.Server = context.Server;
        context.Environment.AppId = appId;
        _config.SaveNamedEnvironmentConfig(context.Directory, context.EnvironmentName, context.Environment);
        _config.SaveProjectEnvironmentSelection(
            context.Directory, new ProjectEnvironmentSelection { Default = context.EnvironmentName });
    }

    private static LinkAppResult CreateFailure(LinkContext context, string message)
    {
        context.Checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, message));
        context.NextSteps.Add("Confirm the app ID or exact slug and selected profile, then rerun automaton link");
        return Failure(context.Checks, context.NextSteps, context.Server);
    }

    private static LinkAppResult Failure(
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        string? server = null)
        => new()
        {
            Success = false,
            Summary = "Link failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            Server = server
        };

    private sealed record AppIdentity(string Id, string Name);
    private sealed record LinkContext(
        string Directory,
        string AppId,
        string Server,
        string ApiKey,
        string? EnvironmentName,
        string ProjectConfigPath,
        AutomatonConfig ProjectConfig,
        GlobalConfig Environment,
        ICollection<CommandDiagnosticCheck> Checks,
        ISet<string> NextSteps);

    private static async Task<JsonElement> GetAppAsync(string server, string apiKey, string appId, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.GetAppByIdOrSlugAsync(appId, cancellationToken);
    }
}
