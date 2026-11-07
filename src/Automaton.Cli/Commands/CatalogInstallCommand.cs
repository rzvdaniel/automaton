using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>automaton catalog list and install: find an app in the server's catalog and install it, set its settings and secrets and deploy it, without Studio.</summary>
public static class CatalogInstallCommand
{
    public static Command BuildList(ConfigService config)
    {
        var search = new Option<string?>("--search", "Only apps whose name, summary or tags match");
        var category = new Option<string?>("--category", "Only this category");
        var source = new Option<string?>("--source", "Only this catalog source id (see automaton catalog sources list)");
        var installed = new Option<bool>("--installed", "Only apps you already installed");
        var json = new Option<bool>("--json", "Print the result as JSON (for agents)");
        var command = new Command("list", "List the apps in the catalog with their source, id and requested permissions.\nExample: automaton catalog list --search flow") { search, category, source, installed, json };
        command.SetHandler(async context =>
        {
            var parsed = context.ParseResult;
            var asJson = parsed.GetValueForOption(json);
            await RunAsync(asJson, async () =>
            {
                using var client = Connect(config);
                var apps = await AllAsync(client, parsed.GetValueForOption(search), parsed.GetValueForOption(category), parsed.GetValueForOption(source), parsed.GetValueForOption(installed));
                if (asJson) { CliJson.Write(new { success = true, apps }); return; }
                RenderList(apps, AnsiConsole.Console);
            });
        });
        return command;
    }

    internal static void RenderList(IReadOnlyList<JsonElement> apps, IAnsiConsole console)
    {
        var table = new Table().AddColumn(new TableColumn("Id").NoWrap()).AddColumn(new TableColumn("Source").NoWrap()).AddColumns("Name", "Category", "Permissions", "Installed");
        foreach (var app in apps)
            table.AddRow(Markup.Escape(Text(app, "id")), Markup.Escape(Text(app, "sourceId")), Markup.Escape(Text(app, "name")), Markup.Escape(Text(app, "category")),
                Markup.Escape(string.Join(", ", Strings(app, "permissions"))), Text(app, "installedAppId").Length > 0 ? "yes" : "");
        console.Write(table);
        console.WriteLine(apps.Count == 0 ? "No apps found." : $"{apps.Count} app(s). Install one with: automaton catalog install <id>");
    }

    public static Command BuildInstall(ConfigService config)
    {
        var id = new Argument<string>("id", "The app id from automaton catalog list");
        var source = new Option<string?>("--source", "Catalog source id, when two sources have an app with this id");
        var name = new Option<string?>("--name", "Name of your install (default: the app's name)");
        var runner = new Option<string?>("--runner", "Runner id or name to run it on (default: the server picks)");
        var setting = new Option<string[]>("--setting", "NAME=VALUE for a setting the app declares; repeat for more (settings are not secret)") { AllowMultipleArgumentsPerToken = false };
        var secretsFile = new Option<string?>("--secrets-file", "File with NAME=VALUE lines for the app's secrets (never pass a secret as an argument)");
        var secretsStdin = new Option<bool>("--secrets-stdin", "Read the secrets as NAME=VALUE lines from standard input");
        var wait = new Option<bool>("--wait", "Wait until the deployment has finished and report whether it succeeded");
        var json = new Option<bool>("--json", "Print the result as JSON (for agents)");
        var command = new Command("install",
            "Install a catalog app, save its settings and secrets and deploy it. It prints the pinned commit and the permissions the app requests first.\n" +
            "Example: automaton catalog install flow-check --name my-flow --runner main --setting INTERVAL=60 --secrets-file ~/flow-secrets --wait\n" +
            "Secrets come only from --secrets-file, --secrets-stdin or, in a terminal, a hidden prompt.")
        { id, source, name, runner, setting, secretsFile, secretsStdin, wait, json };
        command.SetHandler(async context =>
        {
            var parsed = context.ParseResult;
            var asJson = parsed.GetValueForOption(json);
            await RunAsync(asJson, () => InstallAsync(config, parsed.GetValueForArgument(id), parsed.GetValueForOption(source), parsed.GetValueForOption(name),
                parsed.GetValueForOption(runner), parsed.GetValueForOption(setting) ?? [], parsed.GetValueForOption(secretsFile),
                parsed.GetValueForOption(secretsStdin), parsed.GetValueForOption(wait), asJson));
        });
        return command;
    }

    private static async Task InstallAsync(ConfigService config, string id, string? sourceId, string? name, string? runnerReference,
        string[] settingArguments, string? secretsFile, bool secretsStdin, bool wait, bool asJson)
    {
        var settings = CatalogInstallValues.ParseSettings(settingArguments);
        var secrets = CatalogInstallValues.ReadSecrets(secretsFile, secretsStdin);
        using var client = Connect(config);
        var entry = Pick(await AllAsync(client, null, null, sourceId, false), id, sourceId);
        if (entry.TryGetProperty("requiresNewerServer", out var newer) && newer.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException($"{Text(entry, "name")} needs a newer server (version {Text(entry, "minServerVersion")} or later). Nothing was installed.");
        var repo = Text(entry, "repo"); var folder = Text(entry, "folder"); var commit = Text(entry, "commit");
        var installSource = await client.GetCatalogInstallSourceAsync(repo, folder, commit);
        var application = installSource.TryGetProperty("application", out var contract) ? contract : default;

        var (unknown, missingSettings, missingSecrets) = CatalogInstallValues.Check(application, settings, secrets);
        if (unknown.Count > 0) throw new InvalidOperationException($"{Text(entry, "name")} does not declare: {string.Join(", ", unknown)}. Nothing was installed.");
        if (missingSettings.Count > 0)
            throw new InvalidOperationException($"Missing required setting(s): {string.Join(", ", missingSettings)}. Add --setting NAME=VALUE for each. Nothing was installed.");
        if (missingSecrets.Count > 0) AskForSecrets(missingSecrets, secrets, secretsStdin, asJson, id);

        var permissions = Strings(application, "permissions");
        if (!asJson)
        {
            Console.WriteLine($"Installing {Text(entry, "name")} from {repo}{(folder.Length > 0 ? "/" + folder : "")} at commit {commit}.");
            Console.WriteLine(permissions.Count > 0
                ? $"It requests these permissions (an administrator approves them in Studio): {string.Join(", ", permissions)}."
                : "It requests no extra permissions.");
        }
        await CreateAndDeployAsync(client, entry, application, name, runnerReference, settings, secrets, wait, asJson, permissions);
    }

    private static async Task CreateAndDeployAsync(AutomatonApiClient client, JsonElement entry, JsonElement application, string? name, string? runnerReference,
        Dictionary<string, string> settings, Dictionary<string, string> secrets, bool wait, bool asJson, List<string> permissions)
    {
        var repo = Text(entry, "repo"); var folder = Text(entry, "folder"); var commit = Text(entry, "commit");
        string? runnerId = runnerReference is null ? null : RunnerReferenceResolver.Pick(await client.ListRunnersAsync(null), runnerReference, "id", "name");
        var app = await client.InstallCatalogAppAsync(repo, folder, commit, name, runnerId);
        var appId = Text(app, "id");
        try
        {
            foreach (var (key, value) in settings.Concat(secrets).Where(item => application.ValueKind != JsonValueKind.Object || !IsDefault(application, item.Key, item.Value)))
                await client.SetSecretAsync(appId, "app", key, value, null);
            var deployment = await client.DeployStoredSourceAsync(appId, wait);
            var failure = wait ? DeploymentFailure(deployment) : null;
            if (failure is not null) throw new InvalidOperationException(failure);
            var operationId = Text(deployment, "operationId");
            if (asJson) CliJson.Write(new { success = true, appId, name = Text(app, "name"), commit, permissions, deployed = wait, operationId = operationId.Length > 0 ? operationId : null });
            else Console.WriteLine(wait ? $"Installed and deployed {Text(app, "name")} (app id {appId})." : $"Installed {Text(app, "name")} (app id {appId}); deployment started. Check it with: automaton apps");
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            throw new InvalidOperationException($"{exception.Message}\nThe app was created (app id {appId}); fix the problem in Studio or with automaton pull {appId}, then deploy again.", exception);
        }
    }

    /// <summary>A setting left at its default is not saved, so the app keeps following the default.</summary>
    private static bool IsDefault(JsonElement application, string key, string value)
        => CatalogInstallValues.Declared(application, "settings").TryGetValue(key, out var declared)
           && declared.TryGetProperty("default", out var fallback) && fallback.ValueKind == JsonValueKind.String && fallback.GetString() == value;

    private static void AskForSecrets(List<string> missing, Dictionary<string, string> secrets, bool stdinUsed, bool asJson, string id)
    {
        if (asJson || stdinUsed || Console.IsInputRedirected)
            throw new InvalidOperationException(
                $"Missing secret(s): {string.Join(", ", missing)}.\nPut NAME=VALUE lines in a file only you can read and add --secrets-file <file>, or pipe them with --secrets-stdin, " +
                $"or run automaton catalog install {id} in a terminal to be asked for each one (nothing is echoed). Nothing was installed.");
        foreach (var name in missing)
        {
            var value = CatalogInstallValues.PromptHidden(name);
            secrets[name] = value.Length > 0 ? value : throw new InvalidOperationException($"The value for {name} is empty. Nothing was installed.");
        }
    }

    private static string? DeploymentFailure(JsonElement deployment)
    {
        var status = Text(deployment, "status");
        var error = Text(deployment, "error");
        return string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) || error.Length > 0 ? $"The deployment failed: {(error.Length > 0 ? error : "see the app page in Studio")}" : null;
    }

    private static JsonElement Pick(IReadOnlyList<JsonElement> entries, string id, string? sourceId)
    {
        var matches = entries.Where(entry => string.Equals(Text(entry, "id"), id, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No catalog app with id '{id}'{(sourceId is null ? "" : $" in source {sourceId}")}. Run automaton catalog list."),
            _ => throw new InvalidOperationException($"More than one catalog source has '{id}': {string.Join(", ", matches.Select(m => Text(m, "sourceId")))}. Add --source <id>.")
        };
    }

    private static async Task<IReadOnlyList<JsonElement>> AllAsync(AutomatonApiClient client, string? search, string? category, string? source, bool installed)
    {
        var apps = new List<JsonElement>();
        for (var page = 1; page <= 100; page++)
        {
            var result = await client.GetCatalogPageAsync(search, category, source, installed, page);
            if (result.TryGetProperty("unavailable", out var unavailable) && unavailable.ValueKind == JsonValueKind.True && apps.Count == 0)
                throw new InvalidOperationException(Text(result, "message") is { Length: > 0 } message ? message : "The catalog is not available right now.");
            var items = result.TryGetProperty("apps", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(item => item.Clone()).ToList() : [];
            apps.AddRange(items);
            if (items.Count == 0 || apps.Count >= (result.TryGetProperty("total", out var total) ? total.GetInt32() : 0)) break;
        }
        return apps;
    }

    private static AutomatonApiClient Connect(ConfigService config)
    {
        var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
        return new AutomatonApiClient(server, apiKey);
    }

    private static async Task RunAsync(bool asJson, Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or InvalidDataException)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else Console.Error.WriteLine($"Error: {exception.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static List<string> Strings(JsonElement item, string property)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(entry => entry.GetString() ?? "").Where(text => text.Length > 0).ToList() : [];

    private static string Text(JsonElement item, string property)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : string.Empty;
}
