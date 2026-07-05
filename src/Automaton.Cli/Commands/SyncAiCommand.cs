using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class SyncAiCommand
{
    public static Command Build(ConfigService config)
    {
        var adapterOption = new Option<string>(
            "--adapter",
            () => AiGuidanceAdapters.None,
            "Optional AI tool adapter to generate: none, claude, copilot, github-skills, or all");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("sync-ai", "Refresh the generated local AI guidance files in this folder")
        {
            adapterOption,
            jsonOption
        };

        cmd.SetHandler((string adapter, bool asJson) =>
        {
            var cwd = Directory.GetCurrentDirectory();
            var result = Sync(config, cwd, adapter);

            if (asJson)
            {
                CliJson.Write(new
                {
                    success = result.Success,
                    summary = result.Summary,
                    checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                    nextSteps = result.NextSteps,
                    mode = result.Mode,
                    manifestPath = result.ManifestPath,
                    generatedFiles = result.GeneratedFiles
                });
                Environment.ExitCode = result.Success ? 0 : 1;
                return;
            }

            CliDiagnosticsWriter.Write(new CommandDiagnosticResult
            {
                Success = result.Success,
                Summary = result.Summary,
                Checks = result.Checks,
                NextSteps = result.NextSteps
            });

            if (result.Success)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Local AI guidance synced for [bold]{result.Mode}[/] mode");
                AnsiConsole.MarkupLine($"[grey]Manifest:[/] {result.ManifestPath}");
                AnsiConsole.MarkupLine($"[grey]Generated files:[/] {result.GeneratedFiles.Count}");
            }

            Environment.ExitCode = result.Success ? 0 : 1;
        }, adapterOption, jsonOption);

        return cmd;
    }

    private static SyncAiResult Sync(ConfigService config, string directory, string adapter)
    {
        try
        {
            var (server, apiKey) = config.RequireCredentials(directory: directory);
            using var client = new AutomatonApiClient(server, apiKey);
            var references = client.GetAiGuidanceExportAsync().GetAwaiter().GetResult();
            var syncResult = new AiGuidancePackService().Sync(directory, references, adapter);
            return new SyncAiResult
            {
                Success = true,
                Summary = "AI guidance sync complete",
                Checks =
                [
                    new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AiGuidance, true, $"local AI guidance synced ({syncResult.Mode})")
                ],
                Mode = syncResult.Mode,
                ManifestPath = syncResult.ManifestPath,
                GeneratedFiles = syncResult.GeneratedFiles
            };
        }
        catch (Exception ex)
        {
            return new SyncAiResult
            {
                Success = false,
                Summary = "AI guidance sync failed",
                Checks =
                [
                    new CommandDiagnosticCheck(CommandDiagnosticCheckNames.AiGuidance, false, ex.Message)
                ],
                NextSteps =
                [
                    "Fix local file permissions, use a valid --adapter value, or rerun automaton sync-ai"
                ]
            };
        }
    }
}
