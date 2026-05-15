using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LoginCommand
{
    public static Command Build(ConfigService config)
    {
        var serverOption = new Option<string?>("--server", "Automaton server URL");
        var keyOption = new Option<string?>(["--key", "-k"], "API key for authentication");
        var globalOption = new Option<bool>("--global", "Store credentials in ~/.automaton/config.json instead of the current project");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("login", "Store CLI credentials and validate them against the server")
        {
            serverOption,
            keyOption,
            globalOption,
            jsonOption
        };

        cmd.SetHandler(async (string? server, string? key, bool global, bool asJson) =>
        {
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var existing = config.ResolveExistingConfig(global, cwd);

                server ??= PromptForValue("Automaton server URL", existing.Server, secret: false);
                key ??= PromptForValue("API key", existing.ApiKey, secret: true);

                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(key))
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, false, "server URL and API key are required"));
                    nextSteps.Add("Provide --server and --key, or answer both prompts");
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Login failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                server = server.TrimEnd('/');

                using var client = new AutomatonApiClient(server, key);
                var user = await client.GetUserProfileAsync();
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, true, $"credentials validated against {server}"));

                var updated = new GlobalConfig { Server = server, ApiKey = key };
                if (global)
                {
                    config.SaveGlobalConfig(updated);
                }
                else
                {
                    config.SaveProjectLocalConfig(cwd, updated);
                }

                var userName = user.TryGetProperty("name", out var name) ? name.GetString() : null;
                var email = user.TryGetProperty("email", out var emailProperty) ? emailProperty.GetString() : null;
                var userId = user.TryGetProperty("id", out var idProperty) ? idProperty.GetGuid().ToString() : null;
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.UserProfile, true, $"authenticated as {userName ?? email ?? userId}"));
                var result = new CommandDiagnosticResult { Success = true, Summary = "Login complete", Checks = checks, NextSteps = [] };

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = true,
                        summary = result.Summary,
                        checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                        nextSteps = result.NextSteps,
                        authMode = "apiKey",
                        storage = global ? "global" : "local",
                        server,
                        user = new { id = userId, name = userName, email }
                    });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Authenticated as [bold]{userName ?? email ?? userId}[/]");
                AnsiConsole.MarkupLine(global
                    ? "[grey]Credentials saved to ~/.automaton/config.json[/]"
                    : "[grey]Credentials saved to ./.automaton/config.json[/]");
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Authentication, false, ex.Message));
                nextSteps.Add("Verify the server URL and API key, then rerun automaton login");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Login failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, serverOption, keyOption, globalOption, jsonOption);

        return cmd;
    }

    private static string? PromptForValue(string label, string? currentValue, bool secret)
    {
        var suffix = string.IsNullOrWhiteSpace(currentValue) ? string.Empty : " [leave blank to keep current]";
        Console.Write($"{label}{suffix}: ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return currentValue;
        }

        return input;
    }
}