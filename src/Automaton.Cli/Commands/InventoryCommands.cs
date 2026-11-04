using System.CommandLine;
using System.CommandLine.Invocation;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

internal static class InventoryCommands
{
    internal static Command BuildApps(ConfigService config)
        => Build(config, "apps", "List apps in your personal workspace", WriteAppsAsync);

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
