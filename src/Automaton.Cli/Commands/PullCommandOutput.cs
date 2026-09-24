using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class PullCommandOutput
{    internal static void WriteStart(bool nonInteractive, string appName, string targetDir)
    {
        if (nonInteractive)
        {
            Console.WriteLine($"Pulling {appName} -> {targetDir}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold]Pulling[/] [cyan]{Markup.Escape(appName)}[/] → {Markup.Escape(targetDir)}");
        }
    }

    internal static void WriteNoFiles(bool nonInteractive)
    {
        if (nonInteractive)
        {
            Console.WriteLine("No source files found on the server.");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No source files found on the server.[/]");
        }
    }

    internal static void WritePulledApp(bool nonInteractive, string appName, string appId, string targetDirectory)
    {
        if (nonInteractive)
        {
            Console.WriteLine($"Pulled app: {appName}");
            Console.WriteLine($"App ID: {appId}");
            Console.WriteLine($"Local project: {targetDirectory}");
            Console.WriteLine("Edit locally, then run 'automaton deploy'.");
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Pulled app [bold]{Markup.Escape(appName)}[/]");
        AnsiConsole.MarkupLine($"[grey]App ID:[/] {Markup.Escape(appId)}");
        AnsiConsole.MarkupLine($"[grey]Local project:[/] {Markup.Escape(targetDirectory)}");
        AnsiConsole.MarkupLine("[grey]Edit locally, then run 'automaton deploy'.[/]");
    }
}
