using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class CurrentRunnerCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command(
            "current-runner",
            "Show the Runner assigned to the Live environment")
        {
            jsonOption
        };
        command.SetHandler((bool asJson) => CurrentRunnerCommandHandler.HandleAsync(config, asJson), jsonOption);
        return command;
    }
}
