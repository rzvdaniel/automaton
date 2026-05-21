using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

public sealed class NewAppService
{
    private readonly ConfigService _config;
    private readonly AiGuidancePackService _aiGuidancePackService;
    private readonly Func<string, string, string, CancellationToken, Task<(string appId, string name)>> _createAppAsync;

    public NewAppService(
        ConfigService config,
        Func<string, string, string, CancellationToken, Task<(string appId, string name)>>? createAppAsync = null,
        AiGuidancePackService? aiGuidancePackService = null)
    {
        _config = config;
        _aiGuidancePackService = aiGuidancePackService ?? new AiGuidancePackService();
        _createAppAsync = createAppAsync ?? CreateAppAsync;
    }

    public async Task<NewAppResult> CreateAsync(
        string directory,
        string? name,
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
            return new NewAppResult
            {
                Success = false,
                Summary = "New failed",
                Checks = checks,
                NextSteps = nextSteps.ToList()
            };
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, false, "app name is required"));
            nextSteps.Add("Pass a name argument, use --name, or answer the app name prompt");
            return new NewAppResult
            {
                Success = false,
                Summary = "New failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = server
            };
        }

        try
        {
            var (appId, appName) = await _createAppAsync(server, apiKey, name.Trim(), cancellationToken);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, true, $"created app {appId}"));

            _config.SaveProjectConfig(directory, new AutomatonConfig
            {
                AppId = appId,
                Name = appName,
                Server = server,
                Frontend = new FrontendConfig
                {
                    Build = "cd frontend && npm install && npm run build",
                    DeployDir = "frontend/dist"
                },
                Backend = new BackendConfig
                {
                    DeployDir = "backend"
                }
            });

            var guidance = _aiGuidancePackService.Sync(directory);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AiGuidance, true, $"local AI guidance synced ({guidance.Mode})"));

            nextSteps.Add("Run automaton deploy");

            return new NewAppResult
            {
                Success = true,
                Summary = "New complete",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                AppId = appId,
                Name = appName,
                Server = server,
                ConfigPath = Path.Combine(directory, "automaton.json")
            };
        }
        catch (Exception ex)
        {
            var checkName = checks.Any(check => check.Name == CommandDiagnosticCheckNames.CreateApp)
                ? CommandDiagnosticCheckNames.AiGuidance
                : CommandDiagnosticCheckNames.CreateApp;
            checks.Add(new CommandDiagnosticCheck(checkName, false, ex.Message));
            nextSteps.Add(checkName == CommandDiagnosticCheckNames.AiGuidance
                ? "Fix local file permissions or rerun automaton new"
                : "Run automaton whoami to confirm the current server and identity");
            return new NewAppResult
            {
                Success = false,
                Summary = "New failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Server = server
            };
        }
    }

    private static async Task<(string appId, string name)> CreateAppAsync(string server, string apiKey, string name, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.CreateAppAsync(name, cancellationToken);
    }
}
