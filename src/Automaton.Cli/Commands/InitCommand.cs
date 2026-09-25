using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton init creates a new app or links an existing app to this directory.
/// </summary>
public static class InitCommand
{
    public static Command Build(ConfigService config)
    {
        var appOption = new Option<string?>(["--app", "-a"], "Link to an existing app ID or exact slug instead of creating a new one");
        var nameOption = new Option<string?>(["--name", "-n"], "App name (required when creating a new app)");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("init", "Create a new Automaton app or link an existing one to this directory")
        {
            appOption, nameOption, jsonOption
        };
        command.SetHandler(
            (string? appId, string? name, bool asJson) => InitCommandHandler.HandleAsync(config, appId, name, asJson),
            appOption, nameOption, jsonOption);
        return command;
    }
}
