using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class CreateCommand
{
    public static Command Build(ConfigService config)
    {
        var nameOption = new Option<string?>(["--name", "-n"], "App name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Local project profile to link, for example local or live");

        var cmd = new Command("create", "Create a remote Automaton app for the current folder and link the selected local profile")
        {
            nameOption,
            jsonOption,
            environmentOption
        };

        cmd.SetHandler((string? name, bool asJson, string? environmentName) =>
            CreateCommandHandler.HandleAsync(config, name, asJson, environmentName),
            nameOption, jsonOption, environmentOption);

        return cmd;
    }
}
