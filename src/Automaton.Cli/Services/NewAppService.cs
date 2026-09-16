using Automaton.Cli.Models;
using Automaton.AiGuidance;
using Automaton.Core.Models;

namespace Automaton.Cli.Services;

public sealed class NewAppService
{
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

        var currentDirectory = Path.GetFullPath(directory);
        var nearestProjectRoot = _config.FindNearestProjectRoot(directory);
        if (nearestProjectRoot is not null
            && !string.Equals(Path.GetFullPath(nearestProjectRoot), currentDirectory, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.LocalScaffold,
                false,
                $"an Automaton project already exists at '{nearestProjectRoot}'; run automaton new from that project root or a separate folder"));
            nextSteps.Add("Run automaton new from the existing project root or choose a separate local folder");
            return new NewAppResult
            {
                Success = false,
                Summary = "New failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Name = name.Trim(),
                Server = server
            };
        }

        var configPath = Path.Combine(directory, "automaton.json");
        var existingConfig = _config.TryLoadProjectConfig(directory);
        if (File.Exists(configPath) && existingConfig is null)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.LocalScaffold,
                false,
                "automaton.json exists but is invalid or unreadable; fix it before creating an app"));
            nextSteps.Add("Fix automaton.json and rerun automaton new, or use automaton validate for contract diagnostics");
            return new NewAppResult
            {
                Success = false,
                Summary = "New failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Name = name.Trim(),
                Server = server
            };
        }

        var existingEnvironment = _config.LoadNamedEnvironmentConfig(directory, resolvedEnvironmentName);
        var existingAppId = existingEnvironment?.AppId ?? existingConfig?.AppId;
        if (!string.IsNullOrWhiteSpace(existingAppId))
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.CreateApp,
                false,
                $"this project is already linked to app {existingAppId}"));
            nextSteps.Add($"Run automaton deploy to deploy this project, or select another profile with --env <name>");
            return new NewAppResult
            {
                Success = false,
                Summary = "New failed",
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                Name = name.Trim(),
                Server = server
            };
        }

        try
        {
            _config.SaveProjectConfig(directory, BuildProjectConfig(existingConfig, name.Trim()));
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

            var namedEnvironment = _config.LoadNamedEnvironmentConfig(directory, resolvedEnvironmentName) ?? new GlobalConfig();
            namedEnvironment.Server = server;
            namedEnvironment.AppId = appId;
            _config.SaveNamedEnvironmentConfig(directory, resolvedEnvironmentName, namedEnvironment);
            _config.SaveProjectEnvironmentSelection(directory, new ProjectEnvironmentSelection { Default = resolvedEnvironmentName });

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

    private static AutomatonConfig BuildProjectConfig(AutomatonConfig? existingConfig, string name)
    {
        return new AutomatonConfig
        {
            Name = name,
            Application = existingConfig?.Application ?? ApplicationContractDefaults.Create(),
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
