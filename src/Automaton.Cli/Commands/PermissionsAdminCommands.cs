using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

/// <summary>
/// permissions list --pending / approve / reject: a system administrator decides app permissions from the terminal.
/// Needs an admin-scoped key (made in Studio by an administrator, expires, works for nothing else) or an administrator
/// login; an ordinary CLI key is refused by the server.
/// </summary>
internal static class PermissionsAdminCommands
{
    internal const string RunnerMaintain = "runner:maintain";

    internal static Command BuildList(ConfigService config)
    {
        var pending = new Option<bool>("--pending", "Only apps with permissions waiting for a decision (the only mode today)");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("list", "List apps whose requested permissions wait for an administrator\nExample: automaton permissions list --pending") { pending, json };
        command.SetHandler(async context =>
        {
            var asJson = context.ParseResult.GetValueForOption(json);
            await CatalogInstallCommand.RunAsync(asJson, async () =>
            {
                using var client = CatalogInstallCommand.Connect(config);
                var result = await client.GetPendingPermissionsAsync();
                var items = result.GetProperty("items").EnumerateArray().ToList();
                if (asJson) { CliJson.Write(new { success = true, apps = items }); return; }
                if (items.Count == 0) { Console.WriteLine("No app permissions are waiting."); return; }
                foreach (var item in items)
                    Console.WriteLine($"{CatalogInstallCommand.Text(item, "id")}  {CatalogInstallCommand.Text(item, "name")}  waiting: {string.Join(", ", CatalogInstallCommand.Strings(item, "waitingPermissions"))}");
            });
        });
        return command;
    }

    internal static Command BuildDecision(ConfigService config, bool approve)
    {
        var verb = approve ? "approve" : "reject";
        var app = new Argument<string>("app", "App id, or the exact app name of an app with permissions waiting");
        var permission = new Argument<string>("permission", "The permission, e.g. runner:maintain");
        var runner = new Option<string?>("--runner", "Machine name or id; required for runner:maintain, which is decided one machine at a time");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command(verb, approve
            ? "Approve an app permission (system administrator)\nExample: automaton permissions approve harbour runner:maintain --runner build-1"
            : "Reject or revoke an app permission (system administrator)")
        { app, permission, runner, json };
        command.SetHandler(async context =>
        {
            var parsed = context.ParseResult;
            var asJson = parsed.GetValueForOption(json);
            await CatalogInstallCommand.RunAsync(asJson, async () =>
            {
                using var client = CatalogInstallCommand.Connect(config);
                var permissionName = parsed.GetValueForArgument(permission).Trim();
                var appId = await ResolveAppAsync(client, parsed.GetValueForArgument(app).Trim());
                var runnerReference = parsed.GetValueForOption(runner);
                CheckRunnerOption(permissionName, runnerReference);

                JsonElement result;
                if (permissionName == RunnerMaintain)
                {
                    var machines = await client.GetRunnerPermissionsAsync(appId);
                    var runnerId = RunnerReferenceResolver.Pick(machines, runnerReference!.Trim(), "runnerId", "name");
                    result = await client.SetRunnerPermissionApprovalAsync(appId, runnerId, approve);
                }
                else result = await client.SetAppPermissionApprovalAsync(appId, permissionName, approve);

                if (asJson) CliJson.Write(new { success = true, appId, permission = permissionName, approved = approve, result });
                else Console.WriteLine($"{(approve ? "Approved" : "Rejected")} {permissionName}{(runnerReference is null ? "" : $" on {runnerReference}")} for app {appId}.");
            });
        });
        return command;
    }

    internal static void CheckRunnerOption(string permission, string? runner)
    {
        if (permission == RunnerMaintain && string.IsNullOrWhiteSpace(runner))
            throw new InvalidOperationException("runner:maintain is decided one machine at a time: add --runner <machine>.");
        if (permission != RunnerMaintain && !string.IsNullOrWhiteSpace(runner))
            throw new InvalidOperationException("--runner applies only to runner:maintain.");
    }

    /// <summary>The admin-scoped key cannot list apps, so a name is looked up in the pending list.</summary>
    private static async Task<string> ResolveAppAsync(AutomatonApiClient client, string reference)
    {
        if (Guid.TryParse(reference, out _)) return reference;
        var list = await client.GetPendingPermissionsAsync();
        if (list.TryGetProperty("total", out var total) && total.GetInt32() > list.GetProperty("items").GetArrayLength())
            Console.Error.WriteLine("Note: more apps are waiting than this list shows; use the app id if a name is not found.");
        return PickApp(list.GetProperty("items"), reference);
    }

    internal static string PickApp(JsonElement items, string reference)
    {
        var pending = items.EnumerateArray()
            .Where(item => string.Equals(CatalogInstallCommand.Text(item, "name"), reference, StringComparison.OrdinalIgnoreCase)).ToList();
        return pending.Count switch
        {
            1 => CatalogInstallCommand.Text(pending[0], "id"),
            0 => throw new InvalidOperationException($"No app named '{reference}' has permissions waiting. Use its app id (needed to revoke an approved permission)."),
            _ => throw new InvalidOperationException($"More than one waiting app is named '{reference}'; use its app id.")
        };
    }
}
