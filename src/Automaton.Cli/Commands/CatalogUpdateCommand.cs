using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

/// <summary>automaton catalog update: update an app installed from the catalog to the commit the catalog pins now, without Studio. It reads the catalog again first, so a pin merged minutes ago counts.</summary>
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
        var (noSchedules, schedule) = CatalogInstallCommand.ScheduleOptions();
        var command = new Command("update",
            "Update an app installed from the catalog to the commit the catalog pins now and redeploy it; data, settings, secrets and existing schedules stay; default schedules the app lacks (also ones you deleted on purpose) are created again (--no-schedules skips them).\n" +
            "Example: automaton catalog update harbour --wait\n" +
            "Without an update it says so and exits 0. Files edited after the install need --confirm-edited; new secrets need --secrets-file or --secrets-stdin.")
        { app, check, confirmEdited, secretsFile, secretsStdin, wait, noSchedules, schedule, json };
        command.SetHandler(async context =>
        {
            var parsed = context.ParseResult;
            var asJson = parsed.GetValueForOption(json);
            await CatalogInstallCommand.RunAsync(asJson, () => UpdateAsync(config, parsed.GetValueForArgument(app), parsed.GetValueForOption(check),
                parsed.GetValueForOption(confirmEdited), parsed.GetValueForOption(secretsFile), parsed.GetValueForOption(secretsStdin), parsed.GetValueForOption(wait), asJson, parsed.GetValueForOption(noSchedules), parsed.GetValueForOption(schedule) ?? []));
        });
        return command;
    }

    private static async Task UpdateAsync(ConfigService config, string appReference, bool check, bool confirmEdited, string? secretsFile, bool secretsStdin, bool wait, bool asJson, bool noSchedules, string[] scheduleNames)
    {
        var secrets = CatalogInstallValues.WithoutEmpty(CatalogInstallValues.ReadSecrets(secretsFile, secretsStdin));
        using var client = CatalogInstallCommand.Connect(config);
        var app = await client.GetAppByIdOrSlugAsync(appReference);
        var appId = CatalogInstallCommand.Text(app, "id");
        var name = CatalogInstallCommand.Text(app, "name");
        var update = await client.GetCatalogUpdateAsync(appId, refresh: true);
        var installed = CatalogInstallCommand.Text(update, "installedCommit");
        var latest = CatalogInstallCommand.Text(update, "latestCommit");
        var compare = CatalogInstallCommand.Text(update, "compareUrl");
        var available = Flag(update, "available");
        var edited = Flag(update, "edited");
        var newSecrets = CatalogInstallCommand.Strings(update, "newSecrets");
        var newSettings = CatalogInstallCommand.Strings(update, "newSettings");
        var newPermissions = CatalogInstallCommand.Strings(update, "newPermissions");
        var deployFailed = Flag(update, "deployFailed");
        var deployError = CatalogInstallCommand.Text(update, "deployError");
        var running = CatalogInstallCommand.Text(update, "runningCommit");
        var retryOnly = deployFailed && installed == latest;

        if (!available)
        {
            if (asJson) CliJson.Write(new { success = true, appId, updateAvailable = false, installedCommit = installed.Length > 0 ? installed : null });
            else Console.WriteLine($"Up to date: {name} is at {Short(installed)}.");
            return;
        }

        if (!asJson)
        {
            if (deployFailed) Console.WriteLine(FailureText(name, deployError, running, Flag(update, "hasPreviousVersion"), retryOnly ? installed : null, appReference));
            if (!retryOnly) Describe(name, installed, latest, compare, edited, newSettings, newSecrets, newPermissions);
        }
        if (check)
        {
            await ReportCheckAsync(client, app, name, update, noSchedules, scheduleNames, asJson, retryOnly);
            Environment.ExitCode = UpdateAvailableExitCode;
            return;
        }

        if (edited && !confirmEdited)
            throw new InvalidOperationException($"{name} was edited after it was installed; the update replaces those changes. Add --confirm-edited to go on. Nothing was changed.");

        await ApplyAsync(client, app, name, update, confirmEdited, secrets, secretsStdin, wait, asJson, appReference, noSchedules, scheduleNames, retryOnly);
    }

    private static async Task ApplyAsync(AutomatonApiClient client, JsonElement app, string name, JsonElement update, bool confirmEdited,
        Dictionary<string, string> secrets, bool secretsStdin, bool wait, bool asJson, string appReference, bool noSchedules, string[] scheduleNames, bool retry = false)
    {
        var appId = CatalogInstallCommand.Text(app, "id");
        var installed = CatalogInstallCommand.Text(update, "installedCommit");
        var latest = CatalogInstallCommand.Text(update, "latestCommit");
        var newSecrets = CatalogInstallCommand.Strings(update, "newSecrets");
        var application = await ContractAsync(client, app, latest);
        var (plan, _) = await PlanSchedulesAsync(client, app, name, update, noSchedules, scheduleNames);
        var required = await RequiredSecretsAsync(client, application, newSecrets, secrets);
        var missing = required.Where(secret => !secrets.ContainsKey(secret)).ToList();
        if (missing.Count > 0) AskForSecrets(missing, secrets, secretsStdin, asJson, appReference);

        try
        {
            foreach (var (key, value) in secrets) await client.SetSecretAsync(appId, "app", key, value, null);
            var waitFor = wait || plan.Any(change => change.Kind != ScheduleChange.Keep);
            var deployment = await client.UpdateCatalogAppAsync(appId, confirmEdited, waitFor);
            var failure = waitFor ? CatalogInstallCommand.DeploymentFailure(deployment) : null;
            if (failure is not null) throw new InvalidOperationException(failure);
            var (created, updatedSchedules, removedSchedules) = await CatalogScheduleSync.ApplyAsync(client, appId, name, plan);
            wait = waitFor;
            var operationId = CatalogInstallCommand.Text(deployment, "operationId");
            if (asJson) CliJson.Write(new { success = true, appId, updated = true, installedCommit = installed, latestCommit = latest, deployed = wait, schedulesCreated = created, schedulesUpdated = updatedSchedules, schedulesRemoved = removedSchedules, schedulesKept = plan.Where(change => change.Kind == ScheduleChange.Keep).Select(change => change.Name), operationId = operationId.Length > 0 ? operationId : null });
            else Console.WriteLine(retry ? (wait ? $"Redeployed {name} at {Short(latest)}." : $"Retrying the deploy of {name} at {Short(latest)}. Check it with: automaton apps show {appId}") : wait ? $"Updated and deployed {name} to {Short(latest)}." : $"Updating {name} to {Short(latest)}; deployment started. Check it with: automaton apps show {appId}");
            if (!asJson) PrintScheduleChanges(created, updatedSchedules, removedSchedules, plan);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && secrets.Count > 0)
        {
            throw new InvalidOperationException($"{exception.Message}\nSecrets from this run may already be saved on {name} (app id {appId}).", exception);
        }
    }

    /// <summary>The new secrets the person must give: declared by the new version, without a source and not optional. Refuses secrets it does not declare.</summary>
    private static Task<List<string>> RequiredSecretsAsync(AutomatonApiClient client, JsonElement application, List<string> newSecrets, Dictionary<string, string> given)
    {
        if (newSecrets.Count == 0 && given.Count == 0) return Task.FromResult(new List<string>());
        var (unknown, _, missing) = CatalogInstallValues.Check(application, new Dictionary<string, string>(), given);
        if (unknown.Count > 0) throw new InvalidOperationException($"The new version does not declare: {string.Join(", ", unknown)}. Nothing was changed.");
        return Task.FromResult(missing.Where(newSecrets.Contains).ToList());
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

    private static async Task ReportCheckAsync(AutomatonApiClient client, JsonElement app, string name, JsonElement update, bool noSchedules, string[] scheduleNames, bool asJson, bool retryOnly)
    {
        var plan = (await PlanSchedulesAsync(client, app, name, update, noSchedules, scheduleNames)).Plan;
        if (!asJson && plan.Count > 0) Console.WriteLine($"Schedules: {string.Join("; ", plan.Select(CatalogScheduleSync.Describe))}.");
        if (asJson) WriteCheck(update, CatalogInstallCommand.Text(app, "id"), retryOnly, plan);
    }

    /// <summary>The new version's contract (for its required secrets and schedules).</summary>
    private static async Task<JsonElement> ContractAsync(AutomatonApiClient client, JsonElement app, string commit)
    {
        var source = await client.GetCatalogInstallSourceAsync(CatalogInstallCommand.Text(app, "installedFromRepo"), CatalogInstallCommand.Text(app, "installedFromFolder"), commit);
        return source.TryGetProperty("application", out var contract) ? contract : default;
    }

    /// <summary>What the update does to the app's default schedules: --no-schedules none; --schedule only those names, and nothing is removed then.</summary>
    private static async Task<(List<ScheduleChange> Plan, List<JsonElement> Chosen)> PlanSchedulesAsync(AutomatonApiClient client, JsonElement app, string name, JsonElement update, bool noSchedules, string[] scheduleNames)
    {
        var application = await ContractAsync(client, app, CatalogInstallCommand.Text(update, "latestCommit"));
        var chosen = CatalogSchedules.Select(application, noSchedules, scheduleNames);
        if (noSchedules) return ([], chosen);
        var all = CatalogSchedules.Select(application, false, []);
        var installed = CatalogInstallCommand.Text(update, "installedCommit");
        var previous = installed == CatalogInstallCommand.Text(update, "latestCommit") ? all
            : await CatalogScheduleSync.ReadDeclaredAsync(client, CatalogInstallCommand.Text(app, "installedFromRepo"), CatalogInstallCommand.Text(app, "installedFromFolder"), installed);
        var have = CatalogScheduleSync.ForApp(await client.ListActionSchedulesAsync(), CatalogInstallCommand.Text(app, "id"));
        return (CatalogScheduleSync.Plan(name, have, previous, chosen, all, scheduleNames.Length == 0), chosen);
    }

    private static void PrintScheduleChanges(List<string> created, List<string> updated, List<string> removed, List<ScheduleChange> plan)
    {
        if (created.Count > 0) Console.WriteLine($"Created {created.Count} schedule(s): {string.Join(", ", created)}.");
        if (updated.Count > 0) Console.WriteLine($"Updated {updated.Count} schedule(s): {string.Join(", ", plan.Where(change => change.Kind == ScheduleChange.Update).Select(CatalogScheduleSync.Describe))}.");
        if (removed.Count > 0) Console.WriteLine($"Removed {removed.Count} schedule(s): {string.Join(", ", removed)}.");
        foreach (var kept in plan.Where(change => change.Kind == ScheduleChange.Keep)) Console.WriteLine($"Schedule {CatalogScheduleSync.Describe(kept)}.");
    }

    private static void WriteCheck(JsonElement update, string appId, bool retry, List<ScheduleChange> plan)
    {
        var running = CatalogInstallCommand.Text(update, "runningCommit");
        var deployFailed = Flag(update, "deployFailed");
        CliJson.Write(new
        {
            success = true, appId, updateAvailable = true,
            installedCommit = CatalogInstallCommand.Text(update, "installedCommit"), latestCommit = CatalogInstallCommand.Text(update, "latestCommit"),
            compareUrl = CatalogInstallCommand.Text(update, "compareUrl"), edited = Flag(update, "edited"),
            newSettings = CatalogInstallCommand.Strings(update, "newSettings"), newSecrets = CatalogInstallCommand.Strings(update, "newSecrets"),
            newPermissions = CatalogInstallCommand.Strings(update, "newPermissions"),
            deployFailed, deployError = deployFailed ? CatalogInstallCommand.Text(update, "deployError") : null,
            runningCommit = running.Length > 0 ? running : null, retry,
            schedules = plan.Select(change => new { change = change.Kind, name = change.Name, detail = change.Detail })
        });
    }

    private static bool Flag(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    internal static string FailureText(string name, string error, string running, bool hasPrevious, string? retryCommit, string app) =>
        ($"The last deploy of {name} failed: {(error.Length > 0 ? error : "no reason recorded")}\n" +
            $"{(running.Length > 0 ? $"It still runs {Short(running)}." : hasPrevious ? "It still runs the previous version." : "Nothing is running yet.")}{(retryCommit is null ? "" : $" Retry the deploy of {Short(retryCommit)} with: automaton catalog update {app}")}");

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
