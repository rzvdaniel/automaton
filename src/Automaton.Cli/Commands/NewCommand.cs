using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class NewCommand
{
    public static Command Build(ConfigService config)
    {
        var nameArgument = new Argument<string?>("name", () => null, "App name")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var nameOption = new Option<string?>(["--name", "-n"], "App name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Local project profile to link, for example local or live");
        var command = new Command("new", "Create a new Automaton app and link this directory")
        {
            nameArgument,
            nameOption,
            jsonOption,
            environmentOption
        };
        command.SetHandler((string? name, string? nameOverride, bool asJson, string? environmentName) =>
            NewCommandHandler.HandleAsync(config, name, nameOverride, asJson, environmentName),
            nameArgument, nameOption, jsonOption, environmentOption);
        return command;
    }
}
