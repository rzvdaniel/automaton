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

        var cmd = new Command("validate", "Validate the current project before deploy or publish")
        {
            jsonOption,
            strictOption
        };

        cmd.SetHandler(async (bool asJson, bool strict) =>
        {
            var validationService = new ValidationService(config);

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

                var table = new Table();
                table.AddColumn("Check");
                table.AddColumn("Result");
                table.AddColumn("Details");

                foreach (var check in result.Checks)
                {
                    var status = check.IsWarning ? "[yellow]warn[/]" : check.Success ? "[green]pass[/]" : "[red]fail[/]";
                    table.AddRow(check.Name, status, check.Message);
                }

                AnsiConsole.Write(table);

                if (result.NextSteps.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Next steps:[/]");
                    foreach (var nextStep in result.NextSteps)
                    {
                        AnsiConsole.MarkupLine($" - {nextStep}");
                    }
                }

                Environment.ExitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, jsonOption, strictOption);

        return cmd;
    }
}