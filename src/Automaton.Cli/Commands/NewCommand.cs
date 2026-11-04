using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Invocation;

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
        var templateOption = new Option<string?>("--template", "Generate a runnable starter: node, python, dotnet, or static");
        var command = new Command("new", "Create a new Automaton app and link this directory")
        {
            nameArgument,
            nameOption,
            jsonOption,
            environmentOption,
            templateOption
        };
        command.SetHandler((InvocationContext context) =>
            NewCommandHandler.HandleAsync(config, context,
                context.ParseResult.GetValueForArgument(nameArgument),
                context.ParseResult.GetValueForOption(nameOption),
                context.ParseResult.GetValueForOption(jsonOption),
                context.ParseResult.GetValueForOption(environmentOption),
                context.ParseResult.GetValueForOption(templateOption)));
        return command;
    }
}
