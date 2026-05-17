using Automaton.Cli.Models;
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
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var (server, apiKey) = config.ResolveCredentials(directory: cwd);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
                checks.Add(credentialsCheck);

                if (!credentialsCheck.Success || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Whoami failed", Checks = checks, NextSteps = nextSteps.ToList() };
                    if (asJson)
                    {
                        CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                    }
                    else
                    {
                        CliDiagnosticsWriter.Write(failure);
                    }
                    Environment.ExitCode = 1;
                    return;
                }

                using var client = new AutomatonApiClient(server!, apiKey!);
                var user = await client.GetUserProfileAsync();

                var id = user.TryGetProperty("id", out var idProperty) ? idProperty.GetGuid().ToString() : null;
                var name = user.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
                var email = user.TryGetProperty("email", out var emailProperty) ? emailProperty.GetString() : null;
                var publicUsername = user.TryGetProperty("publicUsername", out var usernameProperty) ? usernameProperty.GetString() : null;
                var roles = user.TryGetProperty("roles", out var rolesProperty) && rolesProperty.ValueKind == JsonValueKind.Array
                    ? rolesProperty.EnumerateArray().Select(role => role.GetString()).Where(role => !string.IsNullOrWhiteSpace(role)).ToArray()
                    : Array.Empty<string?>();
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {name ?? email ?? id}"));
                var result = new CommandDiagnosticResult { Success = true, Summary = "Whoami complete", Checks = checks, NextSteps = [] };

                if (asJson)
                {
                    CliJson.Write(new { success = true, summary = result.Summary, checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = result.NextSteps, authMode = "apiKey", server, user = new { id, name, email, publicUsername, roles } });
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
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, false, ex.Message));
                nextSteps.Add("Run automaton setup to refresh or set credentials");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Whoami failed", Checks = checks, NextSteps = nextSteps.ToList() };
                if (asJson)
                {
                    CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                }
                else
                {
                    CliDiagnosticsWriter.Write(failure);
                }
                Environment.ExitCode = 1;
            }
        }, jsonOption);

        return cmd;
    }
}
