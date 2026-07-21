using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;
using Automaton.Core.Models;

namespace Automaton.Cli.Commands;

public static class ActionCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("action", "Validate and run Actions in the linked App");
        command.AddCommand(BuildValidate());
        command.AddCommand(BuildRun(config));
        command.AddCommand(BuildArtifact(config, result: false));
        command.AddCommand(BuildArtifact(config, result: true));
        command.AddCommand(BuildCancel(config));
        command.AddCommand(BuildVersions(config));
        command.AddCommand(BuildRollback(config));
        command.AddCommand(BuildDeleteVersion(config));
        return command;
    }

    private static Command BuildValidate()
    {
        var key = new Argument<string>("action-key");
        var command = new Command("validate", "Validate an App Action manifest and entrypoint") { key };
        command.SetHandler((string actionKey) =>
        {
            var root = Directory.GetCurrentDirectory();
            var actions = AppActionManifestParser.Discover(root);
            var action = actions.SingleOrDefault(item => item.Key == actionKey)
                ?? throw new InvalidOperationException($"Action '{actionKey}' was not found under actions/.");
            var entrypoint = Path.GetFullPath(Path.Combine(action.DirectoryPath, action.Manifest.Spec.Entrypoint));
            if (!File.Exists(entrypoint)) throw new InvalidOperationException($"Entrypoint '{action.Manifest.Spec.Entrypoint}' does not exist.");
            Console.WriteLine($"Action {actionKey} is valid ({action.Manifest.Spec.Runtime}, timeout {action.Manifest.Spec.TimeoutSeconds}s). ");
        }, key);
        return command;
    }

    private static Command BuildRun(ConfigService config)
    {
        var key = new Argument<string>("action-key");
        var input = new Option<string>("--input", () => "{}", "JSON input value");
        var command = new Command("run", "Run an Action on the linked App Runner") { key, input };
        command.SetHandler(async (string actionKey, string inputJson) =>
        {
            using var document = JsonDocument.Parse(inputJson);
            var (server, apiKey, appId) = Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var execution = await client.RunAppActionAsync(appId, actionKey, document.RootElement.Clone());
            var executionId = execution.GetProperty("id").GetGuid();
            var status = execution.GetProperty("status").GetString() ?? "Dispatching";
            while (!IsTerminal(status))
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                execution = await client.GetAppActionExecutionAsync(appId, executionId);
                status = execution.GetProperty("status").GetString() ?? status;
            }
            Console.WriteLine(JsonSerializer.Serialize(execution, new JsonSerializerOptions { WriteIndented = true }));
            if (status == AppModuleExecutionStatuses.Succeeded)
            {
                var artifact = await client.GetAppActionArtifactAsync(appId, executionId, result: true);
                if (artifact.TryGetProperty("available", out var available) && available.GetBoolean())
                    Console.WriteLine(artifact.GetProperty("content").GetString());
            }
            else
            {
                Environment.ExitCode = 1;
            }
        }, key, input);
        return command;
    }

    private static Command BuildArtifact(ConfigService config, bool result)
    {
        var id = new Argument<Guid>("execution-id");
        var command = new Command(result ? "result" : "logs", result ? "Read temporary Action result" : "Read temporary Action logs") { id };
        command.SetHandler(async (Guid executionId) =>
        {
            var (server, apiKey, appId) = Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var artifact = await client.GetAppActionArtifactAsync(appId, executionId, result);
            if (artifact.TryGetProperty("available", out var available) && available.GetBoolean()
                && artifact.TryGetProperty("content", out var content)) Console.WriteLine(content.GetString());
            else Console.Error.WriteLine(artifact.TryGetProperty("message", out var message) ? message.GetString() : "Artifact is unavailable.");
        }, id);
        return command;
    }

    private static Command BuildCancel(ConfigService config)
    {
        var id = new Argument<Guid>("execution-id");
        var command = new Command("cancel", "Cancel an active Action execution") { id };
        command.SetHandler(async (Guid executionId) =>
        {
            var (server, apiKey, appId) = Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var response = await client.CancelAppActionExecutionAsync(appId, executionId);
            Console.WriteLine(response.GetProperty("cancelled").GetBoolean() ? "Cancellation requested." : "Execution is not active.");
        }, id);
        return command;
    }

    private static Command BuildVersions(ConfigService config)
    {
        var key = new Argument<string>("action-key");
        var command = new Command("versions", "List Runner-local Action versions") { key };
        command.SetHandler(async (string actionKey) =>
        {
            var (server, apiKey, appId) = Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var response = await client.ListAppActionVersionsAsync(appId, actionKey);
            foreach (var version in response.GetProperty("versions").EnumerateArray())
                Console.WriteLine($"{version.GetProperty("version").GetString()}\t{version.GetProperty("contentHash").GetString()}\t{version.GetProperty("sizeBytes").GetInt64()} bytes{(version.GetProperty("isCurrent").GetBoolean() ? "\tcurrent" : "")}");
        }, key);
        return command;
    }

    private static Command BuildRollback(ConfigService config)
    {
        var key = new Argument<string>("action-key");
        var version = new Argument<string>("version");
        var command = new Command("rollback", "Restore a Runner-local Action version") { key, version };
        command.SetHandler(async (string actionKey, string versionName) =>
        {
            var (server, apiKey, appId) = Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var response = await client.RestoreAppActionVersionAsync(appId, actionKey, versionName);
            Console.WriteLine(response.GetProperty("message").GetString());
        }, key, version);
        return command;
    }

    private static Command BuildDeleteVersion(ConfigService config)
    {
        var key = new Argument<string>("action-key");
        var version = new Argument<string>("version");
        var command = new Command("delete-version", "Delete a non-current Runner-local Action version") { key, version };
        command.SetHandler(async (string actionKey, string versionName) =>
        {
            var (server, apiKey, appId) = Resolve(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var response = await client.DeleteAppActionVersionAsync(appId, actionKey, versionName);
            Console.WriteLine(response.GetProperty("message").GetString());
        }, key, version);
        return command;
    }

    private static (string Server, string ApiKey, string AppId) Resolve(ConfigService config)
    {
        var directory = Directory.GetCurrentDirectory();
        var resolved = config.ResolveContext(config.TryLoadProjectConfig(directory), directory);
        if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey) || string.IsNullOrWhiteSpace(resolved.AppId))
            throw new InvalidOperationException("The current folder must be linked to an App and authenticated. Run automaton link/login first.");
        return (resolved.Server, resolved.ApiKey, resolved.AppId);
    }

    private static bool IsTerminal(string status) => status is
        AppModuleExecutionStatuses.Succeeded or AppModuleExecutionStatuses.Failed or AppModuleExecutionStatuses.TimedOut
        or AppModuleExecutionStatuses.Cancelled or AppModuleExecutionStatuses.RunnerUnavailable or AppModuleExecutionStatuses.Lost;
}
