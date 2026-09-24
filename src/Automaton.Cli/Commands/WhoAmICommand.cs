using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class WhoAmICommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");

        var cmd = new Command("whoami", "Show the current authenticated Automaton user")
        {
            jsonOption,
            environmentOption
        };

        cmd.SetHandler((bool asJson, string? environmentName) =>
            WhoAmICommandHandler.HandleAsync(config, asJson, environmentName), jsonOption, environmentOption);

        return cmd;
    }
}
