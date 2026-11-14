using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

/// <summary>automaton catalog update: update an app installed from the catalog to the commit the catalog pins now, without Studio.</summary>
public static class CatalogUpdateCommand
{
    /// <summary>Exit code of --check when an update exists (0: up to date, 1: error).</summary>
    public const int UpdateAvailableExitCode = 2;

    public static Command Build(ConfigService config)
    {
        var app = new Argument<string>("app", "App id or slug of an app installed from the catalog");
        var check = new Option<bool>("--check", "Only report: exit 0 when up to date, 2 when an update exists, change nothing");
        var confirmEdited = new Option<bool>("--confirm-edited", "Allow the update to replace files that were edited after the install");
        var secretsFile = new Option<string?>("--secrets-file", "File with NAME=VALUE lines for secrets the new version needs (never pass a secret as an argument)");
        var secretsStdin = new Option<bool>("--secrets-stdin", "Read the secrets as NAME=VALUE lines from standard input");
        var wait = new Option<bool>("--wait", "Wait until the deployment has finished and report whether it succeeded");
        var json = new Option<bool>("--json", "Print the result as JSON (for agents)");
        var command = new Command("update",
            "Update an app installed from the catalog to the commit the catalog pins now and redeploy it; data, settings, secrets and schedules stay.\n" +
            "Example: automaton catalog update harbour --wait\n" +
            "Without an update it says so and exits 0. Files edited after the install need --confirm-edited; new secrets need --secrets-file or --secrets-stdin.")
        { app, check, confirmEdited, secretsFile, secretsStdin, wait, json };
        command.SetHandler(async context =>
        {
            var parsed = context.ParseResult;
            var asJson = parsed.GetValueForOption(json);
            await CatalogInstallCommand.RunAsync(asJson, () => UpdateAsync(config, parsed.GetValueForArgument(app), parsed.GetValueForOption(check),
                parsed.GetValueForOption(confirmEdited), parsed.GetValueForOption(secretsFile), parsed.GetValueForOption(secretsStdin), parsed.GetValueForOption(wait), asJson));
        });
        return command;
    }

    private static async Task UpdateAsync(ConfigService config, string appReference, bool check, bool confirmEdited, string? secretsFile, bool secretsStdin, bool wait, bool asJson)
    {
        var secrets = CatalogInstallValues.WithoutEmpty(CatalogInstallValues.ReadSecrets(secretsFile, secretsStdin));
        using var client = CatalogInstallCommand.Connect(config);
        var app = await client.GetAppByIdOrSlugAsync(appReference);
        var appId = CatalogInstallCommand.Text(app, "id");
        var name = CatalogInstallCommand.Text(app, "name");
        var update = await client.GetCatalogUpdateAsync(appId);
        var installed = CatalogInstallCommand.Text(update, "installedCommit");
        var latest = CatalogInstallCommand.Text(update, "latestCommit");
        var compare = CatalogInstallCommand.Text(update, "compareUrl");
        var available = update.TryGetProperty("available", out var flag) && flag.ValueKind == JsonValueKind.True;
        var edited = update.TryGetProperty("edited", out var editedFlag) && editedFlag.ValueKind == JsonValueKind.True;
        var newSecrets = CatalogInstallCommand.Strings(update, "newSecrets");
        var newSettings = CatalogInstallCommand.Strings(update, "newSettings");
        var newPermissions = CatalogInstallCommand.Strings(update, "newPermissions");

        if (!available)
        {
            if (asJson) CliJson.Write(new { success = true, appId, updateAvailable = false, installedCommit = installed.Length > 0 ? installed : null });
            else Console.WriteLine($"Up to date: {name} is at {Short(installed)}.");
            return;
        }

        if (!asJson) Describe(name, installed, latest, compare, edited, newSettings, newSecrets, newPermissions);
        if (check)
        {
            if (asJson) CliJson.Write(new { success = true, appId, updateAvailable = true, installedCommit = installed, latestCommit = latest, compareUrl = compare, edited, newSettings, newSecrets, newPermissions });
            Environment.ExitCode = UpdateAvailableExitCode;
            return;
        }

        if (edited && !confirmEdited)
            throw new InvalidOperationException($"{name} was edited after it was installed; the update replaces those changes. Add --confirm-edited to go on. Nothing was changed.");

        await ApplyAsync(client, app, name, update, confirmEdited, secrets, secretsStdin, wait, asJson, appReference);
    }

    private static async Task ApplyAsync(AutomatonApiClient client, JsonElement app, string name, JsonElement update, bool confirmEdited,
        Dictionary<string, string> secrets, bool secretsStdin, bool wait, bool asJson, string appReference)
    {
        var appId = CatalogInstallCommand.Text(app, "id");
        var installed = CatalogInstallCommand.Text(update, "installedCommit");
        var latest = CatalogInstallCommand.Text(update, "latestCommit");
        var newSecrets = CatalogInstallCommand.Strings(update, "newSecrets");
        var required = await RequiredSecretsAsync(client, app, latest, newSecrets, secrets);
        var missing = required.Where(secret => !secrets.ContainsKey(secret)).ToList();
        if (missing.Count > 0) AskForSecrets(missing, secrets, secretsStdin, asJson, appReference);

        try
        {
            foreach (var (key, value) in secrets) await client.SetSecretAsync(appId, "app", key, value, null);
            var deployment = await client.UpdateCatalogAppAsync(appId, confirmEdited, wait);
            var failure = wait ? CatalogInstallCommand.DeploymentFailure(deployment) : null;
            if (failure is not null) throw new InvalidOperationException(failure);
            var operationId = CatalogInstallCommand.Text(deployment, "operationId");
            if (asJson) CliJson.Write(new { success = true, appId, updated = true, installedCommit = installed, latestCommit = latest, deployed = wait, operationId = operationId.Length > 0 ? operationId : null });
            else Console.WriteLine(wait ? $"Updated and deployed {name} to {Short(latest)}." : $"Updating {name} to {Short(latest)}; deployment started. Check it with: automaton apps show {appId}");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && secrets.Count > 0)
        {
            throw new InvalidOperationException($"{exception.Message}\nSecrets from this run may already be saved on {name} (app id {appId}).", exception);
        }
    }

    /// <summary>The new secrets the person must give: declared by the new version, without a source and not optional. Refuses secrets it does not declare.</summary>
    private static async Task<List<string>> RequiredSecretsAsync(AutomatonApiClient client, JsonElement app, string latest, List<string> newSecrets, Dictionary<string, string> given)
    {
        if (newSecrets.Count == 0 && given.Count == 0) return [];
        var source = await client.GetCatalogInstallSourceAsync(CatalogInstallCommand.Text(app, "installedFromRepo"), CatalogInstallCommand.Text(app, "installedFromFolder"), latest);
        var application = source.TryGetProperty("application", out var contract) ? contract : default;
        var (unknown, _, missing) = CatalogInstallValues.Check(application, new Dictionary<string, string>(), given);
        if (unknown.Count > 0) throw new InvalidOperationException($"The new version does not declare: {string.Join(", ", unknown)}. Nothing was changed.");
        return missing.Where(newSecrets.Contains).ToList();
    }

    private static void AskForSecrets(List<string> missing, Dictionary<string, string> secrets, bool stdinUsed, bool asJson, string app)
    {
        if (asJson || stdinUsed || Console.IsInputRedirected)
            throw new InvalidOperationException(
                $"The new version needs secret(s): {string.Join(", ", missing)}.\nPut NAME=VALUE lines in a file only you can read and add --secrets-file <file>, or pipe them with --secrets-stdin, " +
                $"or run automaton catalog update {app} in a terminal to be asked for each one (nothing is echoed). Nothing was changed.");
        foreach (var name in missing)
        {
            var value = CatalogInstallValues.PromptHidden(name);
            secrets[name] = value.Length > 0 ? value : throw new InvalidOperationException($"The value for {name} is empty. Nothing was changed.");
        }
    }

    private static void Describe(string name, string installed, string latest, string compare, bool edited, List<string> settings, List<string> secrets, List<string> permissions)
    {
        Console.WriteLine($"{name}: installed {Short(installed)}, catalog pins {Short(latest)}.");
        if (compare.Length > 0) Console.WriteLine($"Changes: {compare}");
        if (settings.Count > 0) Console.WriteLine($"New settings: {string.Join(", ", settings)}");
        if (secrets.Count > 0) Console.WriteLine($"New secrets: {string.Join(", ", secrets)}");
        if (permissions.Count > 0) Console.WriteLine($"New permissions (an administrator approves them in Studio): {string.Join(", ", permissions)}");
        if (edited) Console.WriteLine("The app's files were edited after the install; the update replaces those changes.");
    }

    internal static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;
}
