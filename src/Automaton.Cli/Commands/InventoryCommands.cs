using System.CommandLine;
using System.CommandLine.Invocation;
using Automaton.Cli.Models;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

internal static class InventoryCommands
{
    internal static Command BuildApps(ConfigService config)
    {
        var command = Build(config, "apps", "List apps in your personal workspace", WriteAppsAsync);
        command.AddCommand(BuildShow(config));
        command.AddCommand(BuildDelete(config));
        return command;
    }

    private static Command BuildDelete(ConfigService config)
    {
        var app = new Argument<string>("app", "App ID, exact slug or exact name");
        var yes = new Option<bool>("--yes", "Confirm permanent deletion of the app, its runtime and its data");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("delete", "Permanently delete an app, its runtime and its data") { app, yes, json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var identifier = context.ParseResult.GetValueForArgument(app);
                if (!context.ParseResult.GetValueForOption(yes))
                    throw new InvalidOperationException($"Pass --yes to permanently delete '{identifier}' with its runtime and data. This cannot be undone.");
                var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Run automaton login to connect to your server.");
                using var client = new AutomatonApiClient(server, apiKey);
                var inventory = await client.ListAppInventoryAsync(context.GetCancellationToken());
                var id = ResolveAppId(inventory, identifier);
                var name = inventory.FirstOrDefault(item => item.Id.ToString() == id.ToLowerInvariant())?.Name;
                await client.DeleteAppAsync(id, context.GetCancellationToken());
                if (asJson) CliJson.Write(new { success = true, appId = id, name, deleted = true });
                else Console.WriteLine($"Deleted app {name ?? id} ({id}) with its runtime and data.");
            }
            catch (Exception exception)
            {
                if (asJson) CliJson.Write(new { success = false, error = exception.Message });
                else Console.Error.WriteLine(exception.Message);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    internal static string ResolveAppId(IReadOnlyCollection<AppInventoryItem> apps, string identifier)
    {
        var value = identifier.Trim();
        if (Guid.TryParse(value, out var guid)) return guid.ToString();
        var matches = apps.Where(item => string.Equals(item.PublicUrl, value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase)).DistinctBy(item => item.Id).ToArray();
        if (matches.Length == 0)
            throw new InvalidOperationException($"App '{value}' was not found among your personal apps. Use an app ID, exact slug or exact name; project apps need the app ID.");
        if (matches.Length > 1)
            throw new InvalidOperationException($"More than one app matches '{value}' by name or slug. Use the app ID: {string.Join(", ", matches.Select(item => item.Id.ToString()))}.");
        return matches[0].Id.ToString();
    }

    private static Command BuildShow(ConfigService config)
    {
        var app = new Argument<string>("app", "App ID or exact slug");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("show", "Show an app with its kind, description, pages and OpenAPI path") { app, json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Run automaton login to connect to your server.");
                using var client = new AutomatonApiClient(server, apiKey);
                var details = await client.GetAppByIdOrSlugAsync(context.ParseResult.GetValueForArgument(app), context.GetCancellationToken());
                var declaration = CliAppDeclaration.From(details);
                var id = details.GetProperty("id").GetString();
                var name = details.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (asJson)
                {
                    CliJson.Write(new { success = true, id, name, kind = declaration.Kind, description = declaration.Description, pages = declaration.Pages, openApi = declaration.OpenApi });
                    return;
                }
                Console.WriteLine($"{name} ({id})");
                Console.WriteLine($"Kind: {declaration.Kind}");
                Console.WriteLine($"Description: {declaration.Description ?? "-"}");
                Console.WriteLine($"OpenAPI: {declaration.OpenApi ?? "-"}");
                Console.WriteLine("Pages: " + (declaration.Pages.Count == 0 ? "-" : string.Join(", ", declaration.Pages.Select(page => $"{page.Label} ({page.Path})"))));
            }
            catch (Exception exception)
            {
                if (asJson) CliJson.Write(new { success = false, error = exception.Message });
                else Console.Error.WriteLine(exception.Message);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    internal static Command BuildInstallationStatus(ConfigService config)
        => Build(config, "installation-status", "Show server version, resource counts and last backup", WriteInstallationAsync);

    private static Command Build(
        ConfigService config, string name, string description, Func<AutomatonApiClient, string, bool, CancellationToken, Task> write)
    {
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command(name, description) { json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Run automaton login to connect to your server.");
                using var client = new AutomatonApiClient(server, apiKey);
                await write(client, server, asJson, context.GetCancellationToken());
            }
            catch (Exception exception)
            {
                if (asJson) CliJson.Write(new { success = false, error = exception.Message });
                else Console.Error.WriteLine(exception.Message);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static async Task WriteAppsAsync(AutomatonApiClient client, string server, bool asJson, CancellationToken cancellationToken)
    {
        var inventory = await client.ListAppInventoryAsync(cancellationToken);
        var apps = inventory.Select(app => new
        {
            app.Id,
            app.Name,
            runner = app.Runner?.Name,
            status = app.LiveEnvironment?.Status ?? "Unassigned",
            publicUrl = app.CanLaunchPublicly && !string.IsNullOrWhiteSpace(app.PublicUrl)
                ? CliAppUrlBuilder.BuildPublishedUrl(server, app.PublicUrl)
                : null
        }).ToArray();
        if (asJson)
        {
            CliJson.Write(new { success = true, apps });
            return;
        }
        Console.WriteLine("ID\tName\tRunner\tStatus\tPublic URL");
        foreach (var app in apps)
            Console.WriteLine($"{app.Id}\t{app.Name}\t{app.runner ?? "-"}\t{app.status}\t{app.publicUrl ?? "-"}");
    }

    private static async Task WriteInstallationAsync(AutomatonApiClient client, string server, bool asJson, CancellationToken cancellationToken)
    {
        var status = await client.GetInstallationSummaryAsync(cancellationToken);
        if (asJson)
        {
            CliJson.Write(status);
            return;
        }
        Console.WriteLine($"Version: {status.Version ?? "Unknown"}");
        Console.WriteLine($"Users: {status.Users}");
        Console.WriteLine($"Apps: {status.Apps}");
        Console.WriteLine($"Runners: {status.Runners}");
        Console.WriteLine($"Last backup: {status.LastBackupAt?.ToString("O") ?? "Unknown"}");
    }
}
