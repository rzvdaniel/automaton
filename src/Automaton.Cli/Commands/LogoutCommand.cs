using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LogoutCommand
{
    public static Command Build(ConfigService config)
    {
        var localOption = new Option<bool>("--local", "Clear credentials from the current project instead of ~/.automaton/config.json");
        var allOption = new Option<bool>("--all", "Clear both project-local and global stored credentials");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("logout", "Clear stored CLI credentials") { localOption, allOption, jsonOption };
        command.SetHandler((bool local, bool all, bool asJson) => LogoutCommandHandler.HandleAsync(config, local, all, asJson),
            localOption, allOption, jsonOption);
        return command;
    }
}
