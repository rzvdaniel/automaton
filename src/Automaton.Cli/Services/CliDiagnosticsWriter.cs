using Automaton.Cli.Models;
using Spectre.Console;

namespace Automaton.Cli.Services;

public static class CliDiagnosticsWriter
{
    public static void Write(CommandDiagnosticResult result, string firstColumn = "Check")
    {
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
}