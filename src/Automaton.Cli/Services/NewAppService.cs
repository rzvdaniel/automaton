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
        var validationFailure = ValidateRequest(
            directory, name, server, apiKey, resolvedEnvironmentName, checks, nextSteps);
        if (validationFailure != null)
        {
            return validationFailure;
        }

        var validatedName = name!.Trim();
        var validatedServer = server!;
        var validatedApiKey = apiKey!;

        var configPath = Path.Combine(directory, "automaton.json");
        var existingConfig = _config.TryLoadProjectConfig(directory);

        try
        {
            _config.SaveProjectConfig(directory, BuildProjectConfig(existingConfig, validatedName));
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.LocalScaffold,
                true,
                $"local scaffold created at {Path.Combine(directory, "automaton.json")}"));

            try
            {
                var guidanceReferences = await _getGuidanceAsync(validatedServer, validatedApiKey, cancellationToken);
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

            var (appId, appName) = await _createAppAsync(validatedServer, validatedApiKey, validatedName, cancellationToken);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, true, $"created app {appId}"));

            var namedEnvironment = _config.LoadNamedEnvironmentConfig(directory, resolvedEnvironmentName) ?? new GlobalConfig();
            namedEnvironment.Server = validatedServer;
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

    private NewAppResult? ValidateRequest(
        string directory,
        string? name,
        string? server,
        string? apiKey,
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var credentialFailure = ValidateCredentials(server, apiKey, checks, nextSteps);
        if (credentialFailure != null) return credentialFailure;
        var nameFailure = ValidateName(name, server, checks, nextSteps);
        if (nameFailure != null) return nameFailure;
        var directoryFailure = ValidateProjectDirectory(directory, name, server, checks, nextSteps);
        if (directoryFailure != null) return directoryFailure;
        return ValidateProjectConfig(directory, name, server, environmentName, checks, nextSteps);
    }

    private static NewAppResult? ValidateCredentials(
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

    private static NewAppResult? ValidateName(
        string? name,
        string? server,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        if (!string.IsNullOrWhiteSpace(name)) return null;
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, false, "app name is required"));
        nextSteps.Add("Pass a name argument, use --name, or answer the app name prompt");
        return Failure(checks, nextSteps, server);
    }

    private NewAppResult? ValidateProjectDirectory(
        string directory,
        string? name,
        string? server,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var projectRoot = _config.FindNearestProjectRoot(directory);
        if (projectRoot is null || IsSameDirectory(projectRoot, directory)) return null;
        checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.LocalScaffold,
            false,
            $"an Automaton project already exists at '{projectRoot}'; run automaton new from that project root or a separate folder"));
        nextSteps.Add("Run automaton new from the existing project root or choose a separate local folder");
        return Failure(checks, nextSteps, server, name);
    }

    private NewAppResult? ValidateProjectConfig(
        string directory,
        string? name,
        string? server,
        string? environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var configPath = Path.Combine(directory, "automaton.json");
        var projectConfig = _config.TryLoadProjectConfig(directory);
        if (File.Exists(configPath) && projectConfig == null)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.LocalScaffold,
                false,
                "automaton.json exists but is invalid or unreadable; fix it before creating an app"));
            nextSteps.Add("Fix automaton.json and rerun automaton new, or use automaton validate for contract diagnostics");
            return Failure(checks, nextSteps, server, name);
        }

        var namedEnvironment = _config.LoadNamedEnvironmentConfig(directory, environmentName!);
        var existingAppId = namedEnvironment?.AppId ?? projectConfig?.AppId;
        if (string.IsNullOrWhiteSpace(existingAppId)) return null;
        checks.Add(new CommandDiagnosticCheck(
            CommandDiagnosticCheckNames.CreateApp, false, $"this project is already linked to app {existingAppId}"));
        nextSteps.Add("Run automaton deploy to deploy this project, or select another profile with --env <name>");
        return Failure(checks, nextSteps, server, name);
    }

    private static bool IsSameDirectory(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

    private static NewAppResult Failure(
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        string? server = null,
        string? name = null)
        => new()
        {
            Success = false,
            Summary = "New failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            Name = name?.Trim(),
            Server = server
        };

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
