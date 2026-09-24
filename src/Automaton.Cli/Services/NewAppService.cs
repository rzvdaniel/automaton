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
        var inputFailure = ValidateCredentialsAndName(server, apiKey, name, checks, nextSteps);
        if (inputFailure is not null) return inputFailure;

        var request = ValidateLocalProject(directory, name!, server!, apiKey!, resolvedEnvironmentName, checks, nextSteps);
        if (request is null) return Failure(checks, nextSteps, name, server);
        return await CreateAndLinkAsync(request, cancellationToken, checks, nextSteps);
    }

    private static NewAppResult? ValidateCredentialsAndName(
        string? server,
        string? apiKey,
        string? name,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var credentials = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(credentials);
        if (!credentials.Success || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            return Failure(checks, nextSteps, name, server);
        }

        if (!string.IsNullOrWhiteSpace(name)) return null;
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, false, "app name is required"));
        nextSteps.Add("Pass a name argument, use --name, or answer the app name prompt");
        return Failure(checks, nextSteps, name, server);
    }

    private NewAppRequest? ValidateLocalProject(
        string directory,
        string name,
        string server,
        string apiKey,
        string environmentName,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var nearestProjectRoot = _config.FindNearestProjectRoot(directory);
        if (IsNestedInExistingProject(directory, nearestProjectRoot))
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LocalScaffold, false,
                $"an Automaton project already exists at '{nearestProjectRoot}'; run automaton new from that project root or a separate folder"));
            nextSteps.Add("Run automaton new from the existing project root or choose a separate local folder");
            return null;
        }

        var existingConfig = _config.TryLoadProjectConfig(directory);
        if (File.Exists(Path.Combine(directory, "automaton.json")) && existingConfig is null)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LocalScaffold, false,
                "automaton.json exists but is invalid or unreadable; fix it before creating an app"));
            nextSteps.Add("Fix automaton.json and rerun automaton new, or use automaton validate for contract diagnostics");
            return null;
        }

        var existingEnvironment = _config.LoadNamedEnvironmentConfig(directory, environmentName);
        var existingAppId = existingEnvironment?.AppId ?? existingConfig?.AppId;
        if (!string.IsNullOrWhiteSpace(existingAppId))
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, false,
                $"this project is already linked to app {existingAppId}"));
            nextSteps.Add("Run automaton deploy to deploy this project, or select another profile with --env <name>");
            return null;
        }

        return new NewAppRequest(directory, name.Trim(), server, apiKey, environmentName, existingConfig);
    }

    private async Task<NewAppResult> CreateAndLinkAsync(
        NewAppRequest request,
        CancellationToken cancellationToken,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        try
        {
            CreateLocalScaffold(request, checks);
            await SyncGuidanceAsync(request, cancellationToken, checks, nextSteps);
            var (appId, appName) = await _createAppAsync(request.Server, request.ApiKey, request.Name, cancellationToken);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, true, $"created app {appId}"));
            SaveNewAppLink(request, appId);
            nextSteps.Add("Run automaton deploy");
            return new NewAppResult
            {
                Success = true,
                Summary = "New complete",
                Checks = checks.ToList(),
                NextSteps = nextSteps.ToList(),
                AppId = appId,
                Name = appName,
                Server = request.Server,
                ConfigPath = Path.Combine(request.Directory, "automaton.json")
            };
        }
        catch (Exception exception)
        {
            return CreateFailure(exception, request, checks, nextSteps);
        }
    }

    private void CreateLocalScaffold(NewAppRequest request, ICollection<CommandDiagnosticCheck> checks)
    {
        _config.SaveProjectConfig(request.Directory, BuildProjectConfig(request.ExistingConfig, request.Name));
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LocalScaffold, true,
            $"local scaffold created at {Path.Combine(request.Directory, "automaton.json")}"));
    }

    private async Task SyncGuidanceAsync(
        NewAppRequest request,
        CancellationToken cancellationToken,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        try
        {
            var references = await _getGuidanceAsync(request.Server, request.ApiKey, cancellationToken);
            var guidance = _aiGuidancePackService.Sync(request.Directory, references);
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AiGuidance, true,
                $"local AI guidance synced ({guidance.Mode})"));
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AiGuidance, false,
                $"latest AI guidance unavailable: {exception.Message}", IsWarning: true));
            nextSteps.Add("Run automaton sync-ai later to pull the latest guidance from the server");
        }
    }

    private void SaveNewAppLink(NewAppRequest request, string appId)
    {
        var environment = _config.LoadNamedEnvironmentConfig(request.Directory, request.EnvironmentName) ?? new GlobalConfig();
        environment.Server = request.Server;
        environment.AppId = appId;
        _config.SaveNamedEnvironmentConfig(request.Directory, request.EnvironmentName, environment);
        _config.SaveProjectEnvironmentSelection(request.Directory,
            new ProjectEnvironmentSelection { Default = request.EnvironmentName });
    }

    private static NewAppResult CreateFailure(
        Exception exception,
        NewAppRequest request,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var scaffoldCreated = checks.Any(check => check.Name == CommandDiagnosticCheckNames.LocalScaffold && check.Success);
        var checkName = scaffoldCreated ? CommandDiagnosticCheckNames.CreateApp : CommandDiagnosticCheckNames.LocalScaffold;
        checks.Add(new CommandDiagnosticCheck(checkName, false, exception.Message));
        AddFailureNextSteps(checkName, nextSteps);
        return Failure(checks, nextSteps, request.Name, request.Server,
            scaffoldCreated ? Path.Combine(request.Directory, "automaton.json") : null);
    }

    private static void AddFailureNextSteps(string checkName, ISet<string> nextSteps)
    {
        if (checkName == CommandDiagnosticCheckNames.LocalScaffold)
        {
            nextSteps.Add("Fix local file permissions or rerun automaton new");
            return;
        }

        nextSteps.Add("Retry remote app creation with automaton create");
        nextSteps.Add("Or link an existing app with automaton link <app-id>");
        nextSteps.Add("Run automaton validate to confirm the local scaffold");
        nextSteps.Add("Run automaton whoami to confirm the current server and identity");
    }

    private static bool IsNestedInExistingProject(string directory, string? projectRoot) =>
        projectRoot is not null
        && !string.Equals(Path.GetFullPath(projectRoot), Path.GetFullPath(directory),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static NewAppResult Failure(
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        string? name,
        string? server,
        string? configPath = null) => new()
        {
            Success = false,
            Summary = "New failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList(),
            Name = name?.Trim(),
            Server = server,
            ConfigPath = configPath
        };

    private sealed record NewAppRequest(
        string Directory,
        string Name,
        string Server,
        string ApiKey,
        string EnvironmentName,
        AutomatonConfig? ExistingConfig);

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
