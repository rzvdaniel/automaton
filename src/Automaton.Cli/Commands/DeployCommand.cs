using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton deploy [--clean] [--skip-backend]
///
/// Workflow:
///   1. Read automaton.json for appId + optional build/deployDir
///   2. Run build command if configured
///   3. Zip resolved deploy directory
///   4. Upload zip to server
///   5. Deploy backend scripts (unless --skip-backend)
/// </summary>
public static class DeployCommand
{
    public static Command Build(ConfigService config)
    {
        var cleanOption = new Option<bool>(
            "--clean",
            "Wipe all server-side app files before uploading (full replace)");

        var skipBackendOption = new Option<bool>(
            "--skip-backend",
            "Skip deploying backend Python scripts after upload");

        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Directory to upload (overrides deployDir in automaton.json)");

        var cmd = new Command("deploy", "Build, upload, and deploy the app to Automaton")
        {
            cleanOption,
            skipBackendOption,
            dirOption
        };

        cmd.SetHandler(async (bool clean, bool skipBackend, string? dir) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.LoadProjectConfig(cwd);
                var (server, apiKey) = config.RequireCredentials();

                AnsiConsole.MarkupLine($"[bold]Deploying[/] [cyan]{appConfig.Name}[/] ({appConfig.AppId})");

                // 1. Optional build step
                var buildCmd = appConfig.Build;
                if (!string.IsNullOrWhiteSpace(buildCmd))
                {
                    AnsiConsole.MarkupLine($"[grey]Running build:[/] {buildCmd}");
                    var exitCode = await RunShellCommandAsync(buildCmd, cwd);
                    if (exitCode != 0)
                    {
                        AnsiConsole.MarkupLine($"[red]Build failed (exit {exitCode}). Deploy aborted.[/]");
                        Environment.Exit(1);
                        return;
                    }
                    AnsiConsole.MarkupLine("[green]✓[/] Build complete");
                }

                // 2. Resolve deploy directory
                var deployDir = dir ?? ZipService.ResolveDeployDirectory(cwd, appConfig.DeployDir);
                var relDir = Path.GetRelativePath(cwd, deployDir);
                AnsiConsole.MarkupLine($"[grey]Packing:[/] {relDir}");

                // 3. Zip
                using var zip = ZipService.CreateZip(deployDir);
                var sizeMb = zip.Length / 1_048_576.0;

                // 4. Upload
                using var client = new AutomatonApiClient(server, apiKey);
                JsonElement uploadResult = default;

                await AnsiConsole.Progress()
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask("[cyan]Uploading[/]", maxValue: 1);
                        uploadResult = await client.UploadZipAsync(appConfig.AppId, zip, clean);
                        task.Increment(1);
                    });

                var written = uploadResult.TryGetProperty("written", out var w)
                    ? w.GetArrayLength() : 0;
                var skipped = uploadResult.TryGetProperty("skipped", out var s)
                    ? s.GetArrayLength() : 0;

                AnsiConsole.MarkupLine($"[green]✓[/] Uploaded [bold]{written}[/] file(s), {skipped} skipped ({sizeMb:F1} MB)");

                // 5. Deploy backend
                if (!skipBackend)
                {
                    AnsiConsole.MarkupLine("[grey]Deploying backend...[/]");
                    var deployResult = await client.DeployBackendAsync(appConfig.AppId);

                    var success = deployResult.TryGetProperty("success", out var succ) && succ.GetBoolean();
                    var message = deployResult.TryGetProperty("message", out var msg) ? msg.GetString() : null;
                    var runnerCount = deployResult.TryGetProperty("runnerCount", out var rc) ? rc.GetInt32() : 0;

                    if (success || runnerCount > 0)
                        AnsiConsole.MarkupLine($"[green]✓[/] {message ?? $"Backend deployed to {runnerCount} runner(s)"}");
                    else if (deployResult.TryGetProperty("runnerCount", out var rc2) && rc2.GetInt32() == 0)
                        AnsiConsole.MarkupLine("[yellow]⚠[/] No runner assigned — backend not deployed. Run 'automaton set-runner' first.");
                    else
                        AnsiConsole.MarkupLine($"[yellow]⚠[/] {message ?? "Backend deploy returned no info"}");
                }

                AnsiConsole.MarkupLine("[green bold]Deploy complete.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, cleanOption, skipBackendOption, dirOption);

        return cmd;
    }

    private static async Task<int> RunShellCommandAsync(string command, string workingDir)
    {
        var (shell, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/c {command}")
            : ("/bin/sh", $"-c \"{command}\"");

        var psi = new System.Diagnostics.ProcessStartInfo(shell, args)
        {
            WorkingDirectory = workingDir,
            UseShellExecute  = false
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
