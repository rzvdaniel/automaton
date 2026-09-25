using Automaton.Cli.Services;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class SetRunnerCommand
{
    public static Command Build(ConfigService config)
    {
        var runnerArgument = new Argument<string?>("runner", () => null, "Runner ID, slug, or exact Runner name");
        var clearOption = new Option<bool>("--clear", "Clear the Live target's Runner assignment");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("set-runner", "Assign or clear the Runner for the Live environment")
        {
            runnerArgument,
            clearOption,
            jsonOption
        };
        command.SetHandler((string? runner, bool clear, bool asJson) =>
            SetRunnerCommandHandler.HandleAsync(config, runner, clear, asJson),
            runnerArgument, clearOption, jsonOption);
        return command;
    }
}
