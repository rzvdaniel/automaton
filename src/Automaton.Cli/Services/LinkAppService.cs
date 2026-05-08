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
        CancellationToken cancellationToken = default)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        try
        {
            var app = await _getAppAsync(server, apiKey, appId.Trim(), cancellationToken);
            var appName = app.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString()! : appId.Trim();
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, true, $"linked to app {appId.Trim()}"));

            _config.SaveProjectConfig(directory, new AutomatonConfig
            {
                AppId = appId.Trim(),
                Name = appName,
                Server = server
            });

            nextSteps.Add("Run automaton pull if you want the app's current remote files");

            return new LinkAppResult
            {
                Success = true,
                Summary = "Link complete",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                AppId = appId.Trim(),
                Name = appName,
                Server = server,
                ConfigPath = Path.Combine(directory, "automaton.json")
            };
        }
        catch (Exception ex)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, ex.Message));
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
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
        return await client.GetAppAsync(appId, cancellationToken);
    }
}