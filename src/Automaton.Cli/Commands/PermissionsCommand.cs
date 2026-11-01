using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>
/// Read-only view of the platform permissions the linked app requests (application.permissions). Approving is
/// a system administrator's decision in Studio; this prints the link to hand them.
/// </summary>
public static class PermissionsCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("permissions", "Show the platform permissions this app requests and whether an administrator approved them");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var status = new Command("status", "Show each requested permission's state and the approval link for an administrator") { json };
        status.SetHandler((bool asJson) => HandleStatusAsync(config, asJson), json);
        command.AddCommand(status);
        return command;
    }

    private static async Task HandleStatusAsync(ConfigService config, bool asJson)
    {
        try
        {
            var directory = Directory.GetCurrentDirectory();
            var project = config.TryLoadProjectConfig(directory)
                ?? throw new InvalidOperationException("automaton.json was not found.");
            var resolved = config.ResolveContext(project, directory, null);
            if (string.IsNullOrWhiteSpace(resolved.AppId))
                throw new InvalidOperationException("The project is not linked to an Automaton app.");
            if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
                throw new InvalidOperationException("Automaton server credentials are not configured.");

            using var client = new AutomatonApiClient(resolved.Server, resolved.ApiKey);
            var result = await client.GetPermissionsAsync(resolved.AppId);
            Write(result, project.Application.Permissions ?? [], asJson);
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            Environment.ExitCode = 1;
        }
    }

    private static void Write(JsonElement result, IReadOnlyList<string> localRequests, bool asJson)
    {
        // The server reports the last deployed contract; a request added locally applies after the next deploy.
        var deployed = result.GetProperty("permissions").EnumerateArray()
            .Select(item => item.GetProperty("permission").GetString()).ToHashSet(StringComparer.Ordinal);
        var notDeployed = localRequests.Where(permission => !deployed.Contains(permission)).ToArray();
        if (asJson)
        {
            CliJson.Write(new { success = true, permissions = result.GetProperty("permissions"),
                waiting = result.GetProperty("waiting"),
                approvalUrl = result.TryGetProperty("approvalUrl", out var url) ? url : default,
                notDeployed });
            return;
        }

        if (deployed.Count == 0 && notDeployed.Length == 0)
        {
            AnsiConsole.MarkupLine("This app requests no permissions (application.permissions in automaton.json).");
            return;
        }
        var table = new Table().AddColumns("Permission", "Status");
        foreach (var item in result.GetProperty("permissions").EnumerateArray())
        {
            var state = item.GetProperty("status").GetString() ?? string.Empty;
            table.AddRow(Markup.Escape(item.GetProperty("permission").GetString() ?? string.Empty), state switch
            {
                "Approved" => "[green]approved[/]",
                "Pending" => "[yellow]waiting for approval[/]",
                _ => $"[red]{Markup.Escape(state.ToLowerInvariant())}[/]"
            });
        }
        foreach (var permission in notDeployed)
            table.AddRow(Markup.Escape(permission), "[grey]requested locally; deploy to send the request[/]");
        AnsiConsole.Write(table);
        if (PermissionStatusCheck.Describe(result) is { } waiting)
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(char.ToUpperInvariant(waiting[0]) + waiting[1..])}[/]");
    }
}
