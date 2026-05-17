using Automaton.Cli.Models;
using System.Text.Json;

namespace Automaton.Cli.Services;

public sealed class DeployPreparationService
{
    private readonly ConfigService _config;
    private readonly Func<string, string, string, CancellationToken, Task<RemoteAppValidationInfo>> _loadRemoteAppAsync;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<RunnerSelectionItem>>> _listRunnersAsync;
    private readonly Func<string, string, string, string, CancellationToken, Task> _assignRunnerAsync;

    public DeployPreparationService(
        ConfigService config,
        Func<string, string, string, CancellationToken, Task<RemoteAppValidationInfo>>? loadRemoteAppAsync = null,
        Func<string, string, CancellationToken, Task<IReadOnlyList<RunnerSelectionItem>>>? listRunnersAsync = null,
        Func<string, string, string, string, CancellationToken, Task>? assignRunnerAsync = null)
    {
        _config = config;
        _loadRemoteAppAsync = loadRemoteAppAsync ?? LoadRemoteAppAsync;
        _listRunnersAsync = listRunnersAsync ?? ListRunnersAsync;
        _assignRunnerAsync = assignRunnerAsync ?? AssignRunnerAsync;
    }

    public async Task<DeployPreparationResult> PrepareAsync(
        string directory,
        string? runnerPreference,
        CancellationToken cancellationToken = default)
    {
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checks = new List<DeployCheckResult>();

        var configPath = Path.Combine(directory, "automaton.json");
        var configFileExists = File.Exists(configPath);
        var projectConfig = _config.TryLoadProjectConfig(directory);
        var (server, apiKey) = _config.ResolveCredentials(projectConfig, directory);

        if (!configFileExists || projectConfig == null || string.IsNullOrWhiteSpace(projectConfig.AppId) || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            return new DeployPreparationResult { Success = true };
        }

        if (CountBackendControllers(directory) == 0)
        {
            return new DeployPreparationResult { Success = true };
        }

        RemoteAppValidationInfo remoteApp;
        try
        {
            remoteApp = await _loadRemoteAppAsync(server, apiKey, projectConfig.AppId, cancellationToken);
            checks.Add(new DeployCheckResult(DeployCheckNames.RemoteAppAccess, true, $"remote app found: {projectConfig.AppId}"));
        }
        catch (Exception ex)
        {
            checks.Add(new DeployCheckResult(DeployCheckNames.RemoteAppAccess, false, ex.Message));
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            return new DeployPreparationResult
            {
                Success = false,
                Checks = checks,
                NextSteps = nextSteps.ToList()
            };
        }

        if (!string.IsNullOrWhiteSpace(remoteApp.ExecutionRunnerId))
        {
            checks.Add(new DeployCheckResult(DeployCheckNames.RunnerAssignment, true, $"runner already assigned: {remoteApp.ExecutionRunnerId}"));
            return new DeployPreparationResult
            {
                Success = true,
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                RunnerId = remoteApp.ExecutionRunnerId
            };
        }

        var runners = await _listRunnersAsync(server, apiKey, cancellationToken);
        var connectedRunners = runners.Where(runner => runner.Connected).ToList();

        if (!string.IsNullOrWhiteSpace(runnerPreference))
        {
            return await AssignPreferredRunnerAsync(
                server,
                apiKey,
                projectConfig.AppId,
                runnerPreference,
                checks,
                nextSteps,
                connectedRunners,
                cancellationToken);
        }

        if (connectedRunners.Count == 1)
        {
            var selected = connectedRunners[0];
            checks.Add(new DeployCheckResult(DeployCheckNames.RunnerLookup, true, $"auto-selected connected runner: {selected.Name}"));
            await _assignRunnerAsync(server, apiKey, projectConfig.AppId, selected.Id, cancellationToken);
            checks.Add(new DeployCheckResult(DeployCheckNames.RunnerAssignment, true, $"runner assigned: {selected.Id}"));
            return new DeployPreparationResult
            {
                Success = true,
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                RunnerId = selected.Id,
                RunnerName = selected.Name
            };
        }

        if (connectedRunners.Count == 0)
        {
            checks.Add(new DeployCheckResult(DeployCheckNames.RunnerLookup, false, "no connected runners are available for backend deploy"));
            nextSteps.Add("Create or deploy a runner in Automaton Studio, then rerun automaton deploy");
            nextSteps.Add("If a connected runner already exists, rerun automaton deploy with --runner <runner-id-or-name>");
            return new DeployPreparationResult
            {
                Success = false,
                Checks = checks,
                NextSteps = nextSteps.ToList()
            };
        }

        checks.Add(new DeployCheckResult(DeployCheckNames.RunnerLookup, false, $"multiple connected runners are available ({connectedRunners.Count})"));
        nextSteps.Add("Choose a runner during deploy or rerun with --runner <runner-id-or-name>");
        return new DeployPreparationResult
        {
            Success = false,
            Checks = checks,
            NextSteps = nextSteps.ToList(),
            AvailableRunners = connectedRunners
        };
    }

    private async Task<DeployPreparationResult> AssignPreferredRunnerAsync(
        string server,
        string apiKey,
        string appId,
        string runnerPreference,
        List<DeployCheckResult> checks,
        ISet<string> nextSteps,
        IReadOnlyList<RunnerSelectionItem> connectedRunners,
        CancellationToken cancellationToken)
    {
        var matches = connectedRunners
            .Where(runner => string.Equals(runner.Id, runnerPreference, StringComparison.OrdinalIgnoreCase)
                || string.Equals(runner.Name, runnerPreference, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            checks.Add(new DeployCheckResult(DeployCheckNames.RunnerLookup, false, $"connected runner '{runnerPreference}' not found"));
            nextSteps.Add("Rerun automaton deploy with --runner <runner-id-or-name> using a connected runner");
            return new DeployPreparationResult
            {
                Success = false,
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                AvailableRunners = connectedRunners
            };
        }

        if (matches.Count > 1)
        {
            checks.Add(new DeployCheckResult(DeployCheckNames.RunnerLookup, false, $"multiple connected runners matched '{runnerPreference}'"));
            nextSteps.Add("Use the runner ID instead of the runner name");
            return new DeployPreparationResult
            {
                Success = false,
                Checks = checks,
                NextSteps = nextSteps.ToList(),
                AvailableRunners = connectedRunners
            };
        }

        var selected = matches[0];
        checks.Add(new DeployCheckResult(DeployCheckNames.RunnerLookup, true, $"runner resolved: {selected.Name}"));
        await _assignRunnerAsync(server, apiKey, appId, selected.Id, cancellationToken);
        checks.Add(new DeployCheckResult(DeployCheckNames.RunnerAssignment, true, $"runner assigned: {selected.Id}"));
        return new DeployPreparationResult
        {
            Success = true,
            Checks = checks,
            NextSteps = nextSteps.ToList(),
            RunnerId = selected.Id,
            RunnerName = selected.Name
        };
    }

    private static int CountBackendControllers(string directory)
    {
        var backendDirectory = Path.Combine(directory, "backend");
        return Directory.Exists(backendDirectory)
            ? Directory.GetFiles(backendDirectory, "*.py", SearchOption.AllDirectories).Length
            : 0;
    }

    private static async Task<RemoteAppValidationInfo> LoadRemoteAppAsync(string server, string apiKey, string appId, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        var app = await client.GetAppAsync(appId, cancellationToken);
        var executionRunnerId = app.TryGetProperty("executionRunnerId", out var runnerProperty)
            && runnerProperty.ValueKind == JsonValueKind.String
                ? runnerProperty.GetString()
                : null;
        return new RemoteAppValidationInfo(executionRunnerId);
    }

    private static async Task<IReadOnlyList<RunnerSelectionItem>> ListRunnersAsync(string server, string apiKey, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        var runners = await client.ListRunnersAsync(cancellationToken);
        if (runners.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return runners
            .EnumerateArray()
            .Select(runner =>
            {
                var id = runner.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
                var name = runner.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
                var connected = runner.TryGetProperty("connectionId", out var connectionProperty)
                    && connectionProperty.ValueKind != JsonValueKind.Null
                    && !string.IsNullOrWhiteSpace(connectionProperty.GetString());
                return string.IsNullOrWhiteSpace(id)
                    ? null
                    : new RunnerSelectionItem(id, string.IsNullOrWhiteSpace(name) ? id : name!, connected);
            })
            .Where(runner => runner != null)
            .Cast<RunnerSelectionItem>()
            .ToList();
    }

    private static async Task AssignRunnerAsync(string server, string apiKey, string appId, string runnerId, CancellationToken cancellationToken)
    {
        using var client = new AutomatonApiClient(server, apiKey);
        await client.UpdateAppRunnerAsync(appId, runnerId, cancellationToken);
    }
}
