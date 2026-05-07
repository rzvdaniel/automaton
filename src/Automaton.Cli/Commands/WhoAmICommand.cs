using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class WhoAmICommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("whoami", "Show the current authenticated Automaton user")
        {
            jsonOption
        };

        cmd.SetHandler(async (bool asJson) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var (server, apiKey) = config.RequireCredentials(directory: cwd);

                using var client = new AutomatonApiClient(server, apiKey);
                var user = await client.GetUserProfileAsync();

                var id = user.TryGetProperty("id", out var idProperty) ? idProperty.GetGuid().ToString() : null;
                var name = user.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
                var email = user.TryGetProperty("email", out var emailProperty) ? emailProperty.GetString() : null;
                var publicUsername = user.TryGetProperty("publicUsername", out var usernameProperty) ? usernameProperty.GetString() : null;
                var roles = user.TryGetProperty("roles", out var rolesProperty) && rolesProperty.ValueKind == JsonValueKind.Array
                    ? rolesProperty.EnumerateArray().Select(role => role.GetString()).Where(role => !string.IsNullOrWhiteSpace(role)).ToArray()
                    : Array.Empty<string?>();

                if (asJson)
                {
                    CliJson.Write(new { success = true, authMode = "apiKey", server, user = new { id, name, email, publicUsername, roles } });
                    return;
                }

                var table = new Table();
                table.AddColumn("Field");
                table.AddColumn("Value");
                table.AddRow("server", server);
                table.AddRow("auth", "apiKey");
                table.AddRow("id", id ?? "?");
                table.AddRow("name", name ?? "?");
                table.AddRow("email", email ?? "?");
                table.AddRow("public username", string.IsNullOrWhiteSpace(publicUsername) ? "-" : publicUsername);
                table.AddRow("roles", roles.Length == 0 ? "-" : string.Join(", ", roles!));
                AnsiConsole.Write(table);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, jsonOption);

        return cmd;
    }
}