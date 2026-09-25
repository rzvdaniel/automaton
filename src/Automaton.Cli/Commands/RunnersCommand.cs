using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>Lists the runners available on the current Automaton account.</summary>
public static class RunnersCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("runners", "List runners available on your Automaton account")
        {
            jsonOption
        };
        command.SetHandler((bool asJson) => RunnersCommandHandler.HandleAsync(config, asJson), jsonOption);
        return command;
    }
}
