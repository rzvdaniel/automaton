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
            try
            {
                if (global && all)
                {
                    throw new InvalidOperationException("Use either --global or --all, not both.");
                }

                var cwd = Directory.GetCurrentDirectory();
                var clearLocal = !global || all;
                var clearGlobal = global || all;

                var clearedLocal = clearLocal && config.ClearProjectLocalConfig(cwd);
                var clearedGlobal = clearGlobal && config.ClearGlobalConfig();

                var envServer = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_SERVER"));
                var envApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
                var envOverridesActive = envServer || envApiKey;

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = true,
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
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, globalOption, allOption, jsonOption);

        return cmd;
    }
}