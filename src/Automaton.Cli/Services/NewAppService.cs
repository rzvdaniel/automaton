using Automaton.Cli.Models;
using Automaton.AiGuidance;

namespace Automaton.Cli.Services;

public sealed class NewAppService
{
    private const string FrontendBuildCommand = "cd frontend && npm install && npm run build";
    private const string FrontendDeployDirectory = "frontend/dist";
    private const string BackendDeployDirectory = "backend";

    private readonly ConfigService _config;
    private readonly AiGuidancePackService _aiGuidancePackService;
    private readonly Func<string, string, string, CancellationToken, Task<(string appId, string name)>> _createAppAsync;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<GuidanceSource>>> _getGuidanceAsync;

    public NewAppService(
        ConfigService config,
        Func<string, string, string, CancellationToken, Task<(string appId, string name)>>? createAppAsync = null,
        Func<string, string, CancellationToken, Task<IReadOnlyList<GuidanceSource>>>? getGuidanceAsync = null,
        AiGuidancePackService? aiGuidancePackService = null)
    {
        _config = config;
        _aiGuidancePackService = aiGuidancePackService ?? new AiGuidancePackService();
        _createAppAsync = createAppAsync ?? CreateAppAsync;
        _getGuidanceAsync = getGuidanceAsync ?? GetGuidanceAsync;
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
            var existingConfig = _config.TryLoadProjectConfig(directory);
            _config.SaveProjectConfig(directory, BuildProjectConfig(existingConfig, name.Trim(), server, appId: null));
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.LocalScaffold,
                true,
                $"local scaffold created at {Path.Combine(directory, "automaton.json")}"));

            try
            {
                var guidanceReferences = await _getGuidanceAsync(server, apiKey, cancellationToken);
                var guidance = _aiGuidancePackService.Sync(directory, guidanceReferences);
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AiGuidance, true, $"local AI guidance synced ({guidance.Mode})"));
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(
                    CommandDiagnosticCheckNames.AiGuidance,
                    false,
                    $"latest AI guidance unavailable: {ex.Message}",
                    IsWarning: true));
                nextSteps.Add("Run automaton sync-ai later to pull the latest guidance from the server");
            }

            var (appId, appName) = await _createAppAsync(server, apiKey, name.Trim(), cancellationToken);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, true, $"created app {appId}"));

            _config.SaveProjectConfig(directory, BuildProjectConfig(_config.TryLoadProjectConfig(directory), appName, server, appId));

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
            var checkName = checks.Any(check => check.Name == CommandDiagnosticCheckNames.LocalScaffold)
                ? CommandDiagnosticCheckNames.CreateApp
                : CommandDiagnosticCheckNames.LocalScaffold;
            checks.Add(new CommandDiagnosticCheck(checkName, false, ex.Message));

            if (checkName == CommandDiagnosticCheckNames.LocalScaffold)
            {
                nextSteps.Add("Fix local file permissions or rerun automaton new");
            }
            else
            {
                nextSteps.Add("Retry remote app creation with automaton create");
                nextSteps.Add("Or link an existing app with automaton link <app-id>");
                nextSteps.Add("Run automaton validate to confirm the local scaffold");
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            }

            return new NewAppResult
            {
                Success = false,
                Summary = "New failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Name = name?.Trim(),
                Server = server,
                ConfigPath = checks.Any(check => check.Name == CommandDiagnosticCheckNames.LocalScaffold && check.Success)
                    ? Path.Combine(directory, "automaton.json")
                    : null
            };
        }
    }

    private static AutomatonConfig BuildProjectConfig(AutomatonConfig? existingConfig, string name, string server, string? appId)
    {
        return new AutomatonConfig
        {
            AppId = appId ?? string.Empty,
            Name = name,
            Server = server,
            Frontend = existingConfig?.Frontend ?? new FrontendConfig
            {
                Build = FrontendBuildCommand,
                DeployDir = FrontendDeployDirectory
            },
            Backend = existingConfig?.Backend ?? new BackendConfig
            {
                DeployDir = BackendDeployDirectory
            },
            Source = existingConfig?.Source
        };
    }

    private static async Task<(string appId, string name)> CreateAppAsync(string server, string apiKey, string name, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.CreateAppAsync(name, cancellationToken);
    }

    private static async Task<IReadOnlyList<GuidanceSource>> GetGuidanceAsync(string server, string apiKey, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        return await client.GetAiGuidanceExportAsync(cancellationToken);
    }
}
