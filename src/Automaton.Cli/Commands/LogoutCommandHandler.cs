using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class LogoutCommandHandler
{
    internal static Task HandleAsync(ConfigService config, bool local, bool all, bool asJson)
    {
        if (local && all)
        {
            WriteScopeConflict(asJson);
            Environment.ExitCode = 1;
            return Task.CompletedTask;
        }

        try
        {
            var result = ClearSelectedCredentials(config, local, all);
            if (asJson) WriteSuccessJson(result);
            else WriteSuccessText(result);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            WriteFailure(ex, asJson);
            Environment.ExitCode = 1;
            return Task.CompletedTask;
        }
    }

    private static LogoutResult ClearSelectedCredentials(ConfigService config, bool local, bool all)
    {
        var cwd = Directory.GetCurrentDirectory();
        var clearLocal = local || all;
        var clearGlobal = !local || all;
        var clearedLocal = clearLocal && config.ClearProjectLocalConfig(cwd);
        var clearedGlobal = clearGlobal && (all ? config.ClearGlobalConfig() : config.RemoveSelectedProfile(cwd));
        var envServer = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_SERVER"));
        var envApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
        var envOverridesActive = envServer || envApiKey;
        var scope = all ? "all" : local ? "local" : "global";
        var checks = new List<CommandDiagnosticCheck>
        {
            new(CommandDiagnosticCheckNames.Logout, true, $"credentials cleared for scope {scope}")
        };
        if (envOverridesActive)
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Credentials,
                true, "environment variables still provide credentials", IsWarning: true));
        var nextSteps = envOverridesActive
            ? new[] { "Unset AUTOMATON_SERVER and/or AUTOMATON_API_KEY if you want a fully logged-out state" }
            : [];
        return new LogoutResult(scope, clearLocal, clearGlobal, clearedLocal, clearedGlobal,
            envServer, envApiKey, checks, nextSteps,
            config.GetProjectLocalConfigPath(cwd), config.GlobalConfigPath);
    }

    private static void WriteScopeConflict(bool asJson)
    {
        var check = new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Logout, false, "use either --local or --all, not both");
        var failure = new CommandDiagnosticResult
        {
            Success = false, Summary = "Logout failed", Checks = [check],
            NextSteps = ["Choose one scope: global (default), --local, or --all"]
        };
        if (asJson) WriteFailureJson(failure);
        else CliDiagnosticsWriter.Write(failure);
    }

    private static void WriteSuccessJson(LogoutResult result)
        => CliJson.Write(new
        {
            success = true, summary = "Logout complete",
            checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps = result.NextSteps, scope = result.Scope,
            cleared = new { local = result.ClearedLocal, global = result.ClearedGlobal },
            envOverridesActive = result.EnvOverridesActive,
            envOverrides = new { server = result.EnvServer, apiKey = result.EnvApiKey }
        });

    private static void WriteSuccessText(LogoutResult result)
    {
        if (result.ClearLocal)
            AnsiConsole.MarkupLine(CredentialsMessage(result.ClearedLocal, "project-local", result.LocalPath));
        if (result.ClearGlobal)
            AnsiConsole.MarkupLine(CredentialsMessage(result.ClearedGlobal, "global", result.GlobalPath));
        if (result.EnvOverridesActive)
            WriteEnvironmentWarning(result.EnvServer, result.EnvApiKey);
    }

    internal static string CredentialsMessage(bool cleared, string kind, string path) => cleared
        ? $"[green]✓[/] Cleared {kind} credentials from {Markup.Escape(path)}"
        : $"[grey]-[/] No {kind} credentials found in {Markup.Escape(path)}";

    private static void WriteEnvironmentWarning(bool hasServer, bool hasApiKey)
    {
        var activeVariables = new List<string>();
        if (hasServer) activeVariables.Add("AUTOMATON_SERVER");
        if (hasApiKey) activeVariables.Add("AUTOMATON_API_KEY");
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] Environment variables still provide credentials: {string.Join(", ", activeVariables)}");
    }

    private static void WriteFailure(Exception exception, bool asJson)
    {
        var result = new CommandDiagnosticResult
        {
            Success = false, Summary = "Logout failed",
            Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Logout, false, exception.Message)]
        };
        if (asJson) WriteFailureJson(result);
        else CliDiagnosticsWriter.Write(result);
    }

    private static void WriteFailureJson(CommandDiagnosticResult result)
        => CliJson.Write(new
        {
            success = false, summary = result.Summary,
            checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps = result.NextSteps
        });

    private sealed record LogoutResult(
        string Scope, bool ClearLocal, bool ClearGlobal, bool ClearedLocal, bool ClearedGlobal,
        bool EnvServer, bool EnvApiKey, List<CommandDiagnosticCheck> Checks, string[] NextSteps,
        string LocalPath, string GlobalPath)
    {
        internal bool EnvOverridesActive => EnvServer || EnvApiKey;
    }
}
