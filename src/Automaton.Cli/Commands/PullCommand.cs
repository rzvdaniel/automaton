using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton pull [--dir &lt;target&gt;]
///
/// Downloads editable server-side app source files to the local project directory.
/// Existing local files are overwritten.
/// </summary>
public static class PullCommand
{
    public static Command Build(ConfigService config)
    {
        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Target directory (defaults to current directory)");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable progress rendering and use plain output");

        var cmd = new Command("pull", "Download editable app source files from the server to the local directory")
        {
            dirOption,
            nonInteractiveOption
        };

        cmd.SetHandler(async (string? dir, bool nonInteractiveFlag) =>
        {
            var nonInteractive = CliInteraction.IsNonInteractive(nonInteractiveFlag);
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.LoadProjectConfig(cwd);
                var (server, apiKey) = config.RequireCredentials(appConfig, cwd);
                var targetDir = dir ?? cwd;

                if (nonInteractive)
                {
                    Console.WriteLine($"Pulling {appConfig.Name} -> {targetDir}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[bold]Pulling[/] [cyan]{appConfig.Name}[/] → {targetDir}");
                }

                using var client = new AutomatonApiClient(server, apiKey);
                var filesResult = await client.ListScopedFilesAsync(appConfig.AppId, "source");

                List<string> files = [];

                // The endpoint may return an array or an object with a "files" property
                if (filesResult.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in filesResult.EnumerateArray())
                        files.Add(f.GetString()!);
                }
                else if (filesResult.TryGetProperty("files", out var fa))
                {
                    foreach (var f in fa.EnumerateArray())
                        files.Add(f.GetString()!);
                }

                // Skip automaton.json from pull — it's local-only config
                files = files.Where(f => !f.Equals("automaton.json", StringComparison.OrdinalIgnoreCase)).ToList();

                if (files.Count == 0)
                {
                    if (nonInteractive)
                    {
                        Console.WriteLine("No files found on the server.");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]No files found on the server.[/]");
                    }
                    return;
                }

                var written = 0;
                if (nonInteractive)
                {
                    foreach (var filePath in files)
                    {
                        var content = await client.ReadScopedFileAsync(appConfig.AppId, "source", filePath);
                        var localPath = Path.Combine(targetDir, filePath.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        await File.WriteAllTextAsync(localPath, content);
                        written++;
                    }

                    Console.WriteLine($"Downloaded {written} file(s)");
                }
                else
                {
                    await AnsiConsole.Progress()
                        .StartAsync(async ctx =>
                        {
                            var task = ctx.AddTask("[cyan]Downloading files[/]", maxValue: files.Count);
                            foreach (var filePath in files)
                            {
                                var content = await client.ReadScopedFileAsync(appConfig.AppId, "source", filePath);
                                var localPath = Path.Combine(targetDir, filePath.Replace('/', Path.DirectorySeparatorChar));
                                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                                await File.WriteAllTextAsync(localPath, content);
                                written++;
                                task.Increment(1);
                            }
                        });

                    AnsiConsole.MarkupLine($"[green]✓[/] Downloaded [bold]{written}[/] file(s)");
                }
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
                Environment.Exit(1);
            }
        }, dirOption, nonInteractiveOption);

        return cmd;
    }
}
