using Automaton.Cli.Models;
using Spectre.Console;

namespace Automaton.Cli.Services;

public static class CliDiagnosticsWriter
{
    public static void Write(CommandDiagnosticResult result, string firstColumn = "Check", bool nonInteractive = false)
    {
        if (nonInteractive)
        {
            WritePlain(result, firstColumn);
            return;
        }

        var table = new Table();
        table.AddColumn(firstColumn);
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
    }

    private static void WritePlain(CommandDiagnosticResult result, string firstColumn)
    {
        foreach (var check in result.Checks)
        {
            var status = check.IsWarning ? "warn" : check.Success ? "pass" : "fail";
            Console.WriteLine($"{firstColumn}: {check.Name} | {status} | {check.Message}");
        }

        if (result.NextSteps.Count > 0)
        {
            Console.WriteLine("Next steps:");
            foreach (var nextStep in result.NextSteps)
            {
                Console.WriteLine($" - {nextStep}");
            }
        }
    }
}
