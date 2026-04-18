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
/// Manages global CLI configuration stored in ~/.automaton/config.json.
/// Environment variables AUTOMATON_SERVER and AUTOMATON_API_KEY always take precedence.
/// </summary>
public static class ConfigCommand
{
    public static Command Build(ConfigService config)
    {
        var serverOption = new Option<string?>("--server", "Automaton server URL (e.g. https://automaton.example.com)");
        var keyOption    = new Option<string?>(["--key", "-k"], "API key (ak_execute_xxx)");
        var showOption   = new Option<bool>("--show", "Print current configuration");

        var cmd = new Command("config", "View or update global CLI settings")
        {
            serverOption,
            keyOption,
            showOption
        };

        cmd.SetHandler((string? server, string? key, bool show) =>
        {
            if (show || (server == null && key == null))
            {
                var global = config.LoadGlobalConfig();
                var table = new Table();
                table.AddColumn("Setting");
                table.AddColumn("Value");
                table.AddColumn("Source");

                AddRow(table, "server",  global.Server,  "AUTOMATON_SERVER",  "~/.automaton/config.json");
                AddRow(table, "api-key", MaskKey(global.ApiKey), "AUTOMATON_API_KEY", "~/.automaton/config.json");

                AnsiConsole.Write(table);
                return;
            }

            var current = config.LoadGlobalConfig();
            var updated = new GlobalConfig
            {
                Server = server ?? current.Server,
                ApiKey = key    ?? current.ApiKey
            };

            config.SaveGlobalConfig(updated);
            AnsiConsole.MarkupLine("[green]✓[/] Configuration saved to ~/.automaton/config.json");

        }, serverOption, keyOption, showOption);

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
