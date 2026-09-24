using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class StatusCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");
        var command = new Command("status", "Show the current linked app, deploy settings, and publish state")
        {
            jsonOption,
            environmentOption
        };
        command.SetHandler((bool asJson, string? environmentName) =>
            StatusCommandHandler.HandleAsync(config, asJson, environmentName), jsonOption, environmentOption);
        return command;
    }
}
