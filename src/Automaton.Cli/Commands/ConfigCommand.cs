using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton config --server &lt;url&gt; --key &lt;apiKey&gt;
/// automaton config --show
///
/// Manages project-local CLI configuration in ./.automaton/config.json by default.
/// Use --global for shared settings in ~/.automaton/config.json.
/// Environment variables AUTOMATON_SERVER and AUTOMATON_API_KEY always take precedence.
/// </summary>
public static class ConfigCommand
{
    public static Command Build(ConfigService config)
    {
        var serverOption = new Option<string?>("--server", "Automaton server URL (e.g. https://automaton.example.com)");
        var keyOption    = new Option<string?>(["--key", "-k"], "API key (ak_execute_xxx)");
        var pythonOption = new Option<string?>("--python", "Preferred Python executable for local app commands");
        var showOption   = new Option<bool>("--show", "Print current configuration");
        var localOption  = new Option<bool>("--local", "Read or write ./.automaton/config.json for the current project (default)");
        var globalOption = new Option<bool>("--global", "Read or write ~/.automaton/config.json instead of project-local config");

        var cmd = new Command("config", "View or update global CLI settings")
        {
            serverOption,
            keyOption,
            pythonOption,
            showOption,
            localOption,
            globalOption
        };

        cmd.SetHandler((string? server, string? key, string? python, bool show, bool local, bool global) =>
        {
            if (local && global)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Choose either --local or --global, not both.");
                Environment.Exit(1);
                return;
            }

            var cwd = Directory.GetCurrentDirectory();
            var useLocal = !global;
            var current = useLocal
                ? config.LoadProjectLocalConfig(cwd)
                : config.LoadGlobalConfig();
            var sourcePath = useLocal
                ? "./.automaton/config.json"
                : "~/.automaton/config.json";

            if (show || (server == null && key == null))
            {
                var table = new Table();
                table.AddColumn("Setting");
                table.AddColumn("Value");
                table.AddColumn("Source");

                AddRow(table, "server", current.Server, "AUTOMATON_SERVER", sourcePath);
                AddRow(table, "api-key", MaskKey(current.ApiKey), "AUTOMATON_API_KEY", sourcePath);
                AddRow(table, "python", current.PythonPath, "AUTOMATON_PYTHON", sourcePath);

                AnsiConsole.Write(table);
                return;
            }

            var updated = new GlobalConfig
            {
                Server = server ?? current.Server,
                ApiKey = key    ?? current.ApiKey,
                PythonPath = python ?? current.PythonPath
            };

            if (useLocal)
            {
                config.SaveProjectLocalConfig(cwd, updated);
                AnsiConsole.MarkupLine("[green]✓[/] Configuration saved to ./.automaton/config.json");
            }
            else
            {
                config.SaveGlobalConfig(updated);
                AnsiConsole.MarkupLine("[green]✓[/] Configuration saved to ~/.automaton/config.json");
            }

        }, serverOption, keyOption, pythonOption, showOption, localOption, globalOption);

        return cmd;
    }

    private static void AddRow(Table table, string name, string? value, string envVar, string fileSource)
    {
        var envValue = Environment.GetEnvironmentVariable(envVar);
        var (display, source) = envValue != null
            ? (MaskKey(envValue), $"[cyan]{envVar}[/] (env)")
            : (value ?? "[grey]not set[/]", fileSource);

        table.AddRow(name, display ?? "[grey]not set[/]", source);
    }

    private static string? MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 8) return key;
        return key[..8] + new string('*', key.Length - 8);
    }
}
