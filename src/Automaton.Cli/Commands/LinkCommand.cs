using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LinkCommand
{
    public static Command Build(ConfigService config)
    {
        var appIdArgument = new Argument<string?>(
            "app-id-or-slug", () => null, "Existing Automaton app ID or exact app slug to link to this directory")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Local project profile to link, for example local or live");
        var command = new Command("link", "Link this directory to an existing Automaton app")
        {
            appIdArgument,
            jsonOption,
            environmentOption
        };
        command.SetHandler((string? appId, bool asJson, string? environmentName) =>
            LinkCommandHandler.HandleAsync(config, appId, asJson, environmentName),
            appIdArgument, jsonOption, environmentOption);
        return command;
    }
}
