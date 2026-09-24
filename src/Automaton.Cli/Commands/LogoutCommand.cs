using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LogoutCommand
{
    public static Command Build(ConfigService config)
    {
        var localOption = new Option<bool>("--local", "Clear credentials from the current project instead of ~/.automaton/config.json");
        var allOption = new Option<bool>("--all", "Clear both project-local and global stored credentials");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("logout", "Clear stored CLI credentials")
        {
            localOption,
            allOption,
            jsonOption
        };

        cmd.SetHandler(
            (bool local, bool all, bool asJson) => HandleLogout(config, local, all, asJson),
            localOption,
            allOption,
            jsonOption);

        return cmd;
    }

    private static void HandleLogout(ConfigService config, bool local, bool all, bool asJson)
    {
        if (local && all)
        {
            WriteFailure(
                "use either --local or --all, not both",
                ["Choose one scope: global (default), --local, or --all"],
                asJson);
            return;
        }

        try
        {
            var outcome = ClearCredentials(config, local, all);
            WriteSuccess(outcome, local, all, asJson);
        }
        catch (Exception ex)
        {
            WriteFailure(ex.Message, [], asJson);
        }
    }

    private static (bool ClearLocal, bool ClearGlobal, bool ClearedLocal, bool ClearedGlobal, bool EnvServer, bool EnvApiKey)
        ClearCredentials(ConfigService config, bool local, bool all)
    {
        var clearLocal = local || all;
        var clearGlobal = !local || all;
        var cwd = Directory.GetCurrentDirectory();
        var clearedLocal = clearLocal && config.ClearProjectLocalConfig(cwd);
        var clearedGlobal = clearGlobal && config.ClearGlobalConfig();
        var envServer = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_SERVER"));
        var envApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));

        return (clearLocal, clearGlobal, clearedLocal, clearedGlobal, envServer, envApiKey);
    }

    private static void WriteSuccess(
        (bool ClearLocal, bool ClearGlobal, bool ClearedLocal, bool ClearedGlobal, bool EnvServer, bool EnvApiKey) outcome,
        bool local,
        bool all,
        bool asJson)
    {
        var checks = CreateSuccessChecks(local, all, outcome.EnvServer || outcome.EnvApiKey);
        var nextSteps = outcome.EnvServer || outcome.EnvApiKey
            ? new[] { "Unset AUTOMATON_SERVER and/or AUTOMATON_API_KEY if you want a fully logged-out state" }
            : Array.Empty<string>();

        if (asJson)
        {
            WriteJsonSuccess(outcome, checks, nextSteps, local, all);
            return;
        }

        WriteConsoleSuccess(outcome);
    }

    private static IReadOnlyList<CommandDiagnosticCheck> CreateSuccessChecks(bool local, bool all, bool envOverridesActive)
    {
        var scope = all ? "all" : local ? "local" : "global";
        var checks = new List<CommandDiagnosticCheck>
        {
            new(CommandDiagnosticCheckNames.Logout, true, $"credentials cleared for scope {scope}")
        };
        if (envOverridesActive)
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials, true, "environment variables still provide credentials", IsWarning: true));

        return checks;
    }

    private static void WriteJsonSuccess(
        (bool ClearLocal, bool ClearGlobal, bool ClearedLocal, bool ClearedGlobal, bool EnvServer, bool EnvApiKey) outcome,
        IReadOnlyList<CommandDiagnosticCheck> checks,
        IReadOnlyList<string> nextSteps,
        bool local,
        bool all)
    {
        CliJson.Write(new
        {
            success = true,
            summary = "Logout complete",
            checks = checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps,
            scope = all ? "all" : local ? "local" : "global",
            cleared = new { local = outcome.ClearedLocal, global = outcome.ClearedGlobal },
            envOverridesActive = outcome.EnvServer || outcome.EnvApiKey,
            envOverrides = new { server = outcome.EnvServer, apiKey = outcome.EnvApiKey }
        });
    }

    private static void WriteConsoleSuccess(
        (bool ClearLocal, bool ClearGlobal, bool ClearedLocal, bool ClearedGlobal, bool EnvServer, bool EnvApiKey) outcome)
    {
        if (outcome.ClearLocal)
        {
            AnsiConsole.MarkupLine(outcome.ClearedLocal
                ? "[green]✓[/] Cleared project-local credentials from ./.automaton/config.json"
                : "[grey]-[/] No project-local credentials found in ./.automaton/config.json");
        }

        if (outcome.ClearGlobal)
        {
            AnsiConsole.MarkupLine(outcome.ClearedGlobal
                ? "[green]✓[/] Cleared global credentials from ~/.automaton/config.json"
                : "[grey]-[/] No global credentials found in ~/.automaton/config.json");
        }

        WriteEnvironmentWarning(outcome.EnvServer, outcome.EnvApiKey);
    }

    private static void WriteEnvironmentWarning(bool envServer, bool envApiKey)
    {
        if (!envServer && !envApiKey)
            return;

        var activeVariables = new List<string>();
        if (envServer) activeVariables.Add("AUTOMATON_SERVER");
        if (envApiKey) activeVariables.Add("AUTOMATON_API_KEY");
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Environment variables still provide credentials: {string.Join(", ", activeVariables)}");
    }

    private static void WriteFailure(
        string message,
        IReadOnlyList<string> nextSteps,
        bool asJson)
    {
        var failure = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Logout failed",
            Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Logout, false, message)],
            NextSteps = nextSteps
        };

        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = failure.Summary,
                checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = failure.NextSteps
            });
        }
        else
        {
            CliDiagnosticsWriter.Write(failure);
        }

        Environment.ExitCode = 1;
    }
}
