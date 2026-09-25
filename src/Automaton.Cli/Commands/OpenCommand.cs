using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

public static class OpenCommand
{
    public static Command Build(ConfigService config)
    {
        var publicOption = new Option<bool>("--public", "Open the published public app URL");
        var privateOption = new Option<bool>("--private", "Open the app in Studio (legacy alias)");
        var targetArgument = new Argument<string?>("environment", () => null, "Environment slug to open; omit for Live")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");
        var command = new Command("open", "Open the current app in the browser")
        {
            targetArgument,
            publicOption,
            privateOption,
            environmentOption
        };
        command.SetHandler((InvocationContext context) =>
            OpenCommandHandler.HandleAsync(config, context, targetArgument, publicOption, privateOption, environmentOption));
        return command;
    }
}
