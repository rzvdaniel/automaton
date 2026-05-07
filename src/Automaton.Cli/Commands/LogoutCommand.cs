using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LogoutCommand
{
    public static Command Build(ConfigService config)
    {
        var globalOption = new Option<bool>("--global", "Clear credentials from ~/.automaton/config.json");
        var allOption = new Option<bool>("--all", "Clear both project-local and global stored credentials");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("logout", "Clear stored CLI credentials")
        {
            globalOption,
            allOption,
            jsonOption
        };

        cmd.SetHandler((bool global, bool all, bool asJson) =>
        {
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (global && all)
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Logout, false, "use either --global or --all, not both"));
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Logout failed", Checks = checks, NextSteps = ["Choose one scope: local (default), --global, or --all"] };
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

                var cwd = Directory.GetCurrentDirectory();
                var clearLocal = !global || all;
                var clearGlobal = global || all;

                var clearedLocal = clearLocal && config.ClearProjectLocalConfig(cwd);
                var clearedGlobal = clearGlobal && config.ClearGlobalConfig();

                var envServer = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_SERVER"));
                var envApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
                var envOverridesActive = envServer || envApiKey;
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Logout, true, $"credentials cleared for scope { (all ? "all" : global ? "global" : "local") }"));
                if (envOverridesActive)
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials, true, "environment variables still provide credentials", IsWarning: true));
                }
                var result = new CommandDiagnosticResult { Success = true, Summary = "Logout complete", Checks = checks, NextSteps = envOverridesActive ? ["Unset AUTOMATON_SERVER and/or AUTOMATON_API_KEY if you want a fully logged-out state"] : [] };

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = true,
                        summary = result.Summary,
                        checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                        nextSteps = result.NextSteps,
                        scope = all ? "all" : global ? "global" : "local",
                        cleared = new { local = clearedLocal, global = clearedGlobal },
                        envOverridesActive,
                        envOverrides = new { server = envServer, apiKey = envApiKey }
                    });
                    return;
                }

                if (clearLocal)
                {
                    AnsiConsole.MarkupLine(clearedLocal
                        ? "[green]✓[/] Cleared project-local credentials from ./.automaton/config.json"
                        : "[grey]-[/] No project-local credentials found in ./.automaton/config.json");
                }

                if (clearGlobal)
                {
                    AnsiConsole.MarkupLine(clearedGlobal
                        ? "[green]✓[/] Cleared global credentials from ~/.automaton/config.json"
                        : "[grey]-[/] No global credentials found in ~/.automaton/config.json");
                }

                if (envOverridesActive)
                {
                    var activeVars = new List<string>();
                    if (envServer) activeVars.Add("AUTOMATON_SERVER");
                    if (envApiKey) activeVars.Add("AUTOMATON_API_KEY");

                    AnsiConsole.MarkupLine($"[yellow]Warning:[/] Environment variables still provide credentials: {string.Join(", ", activeVars)}");
                }
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Logout, false, ex.Message));
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Logout failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, globalOption, allOption, jsonOption);

        return cmd;
    }
}