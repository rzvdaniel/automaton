using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class ValidateCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var strictOption = new Option<bool>("--strict", "Treat warnings as failures");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable rich terminal rendering and use plain output");

        var cmd = new Command("validate", "Validate the current project before deploy or publish")
        {
            jsonOption,
            strictOption,
            nonInteractiveOption
        };

        cmd.SetHandler(async (bool asJson, bool strict, bool nonInteractiveFlag) =>
        {
            var validationService = new ValidationService(config);
            var nonInteractive = CliInteraction.IsNonInteractive(nonInteractiveFlag, asJson);

            try
            {
                var result = await validationService.ValidateAsync(Directory.GetCurrentDirectory(), strict: strict);

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = result.Success,
                        summary = result.Summary,
                        checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                        nextSteps = result.NextSteps
                    });
                    Environment.ExitCode = result.Success ? 0 : 1;
                    return;
                }

                CliDiagnosticsWriter.Write(new CommandDiagnosticResult
                {
                    Success = result.Success,
                    Summary = result.Summary,
                    Checks = result.Checks.Select(check => new CommandDiagnosticCheck(check.Name, check.Success, check.Message, check.IsWarning)).ToList(),
                    NextSteps = result.NextSteps
                }, nonInteractive: nonInteractive);

                Environment.ExitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                if (nonInteractive)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                }
                Environment.ExitCode = 1;
            }
        }, jsonOption, strictOption, nonInteractiveOption);

        return cmd;
    }
}
