using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton pull
///   Refresh the current linked local project from remote source files.
///
/// automaton pull &lt;app-id-or-slug&gt;
///   Create a new local folder, write automaton.json, and download remote source files.
/// </summary>
public static class PullCommand
{
    private const string FrontendBuildCommand = "cd frontend && npm install && npm run build";
    private const int OverwritePreviewCount = 10;

    public static Command Build(ConfigService config)
    {
        var appArgument = new Argument<string?>("app", () => null, "App ID or slug to pull into a new local folder");
        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Target directory (defaults to current directory for refresh mode, or a new subfolder in the current directory for app mode)");
        var forceOption = new Option<bool>("--force", "Overwrite matching local files without prompting");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable progress rendering and use plain output");

        var cmd = new Command("pull", "Download editable app source files from the server")
        {
            appArgument,
            dirOption,
            forceOption,
            nonInteractiveOption
        };

        cmd.SetHandler(async (string? app, string? dir, bool force, bool nonInteractiveFlag) =>
        {
            var nonInteractive = CliInteraction.IsNonInteractive(nonInteractiveFlag);

            try
            {
                var cwd = Directory.GetCurrentDirectory();

                if (string.IsNullOrWhiteSpace(app))
                {
                    await PullIntoLinkedProjectAsync(config, cwd, dir, force, nonInteractive);
                    return;
                }

                await PullIntoNewProjectAsync(config, cwd, app.Trim(), dir, nonInteractive);
            }
            catch (Exception ex)
            {
                if (nonInteractive)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                }

                Environment.Exit(1);
            }
        }, appArgument, dirOption, forceOption, nonInteractiveOption);

        return cmd;
    }

    private static async Task PullIntoLinkedProjectAsync(ConfigService config, string cwd, string? dir, bool force, bool nonInteractive)
    {
        var appConfig = config.LoadProjectConfig(cwd);
        var (server, apiKey) = config.RequireCredentials(appConfig, cwd);
        var targetDir = dir ?? cwd;

        WriteStart(nonInteractive, appConfig.Name, targetDir);

        using var client = new AutomatonApiClient(server, apiKey);
        var files = await ListSourceFilesAsync(client, appConfig.AppId);
        var sourceFiles = files.Where(IsPortableSourceFile).ToList();

        if (sourceFiles.Count == 0)
        {
            WriteNoFiles(nonInteractive);
            return;
        }

        var overwritePaths = GetOverwritePaths(sourceFiles, targetDir);
        EnsureOverwriteApproved(overwritePaths, force, nonInteractive);

        await DownloadFilesAsync(client, appConfig.AppId, sourceFiles, targetDir, nonInteractive);
    }

    private static async Task PullIntoNewProjectAsync(ConfigService config, string cwd, string appIdentifier, string? dir, bool nonInteractive)
    {
        var (server, apiKey) = config.RequireCredentials(directory: cwd);
        using var client = new AutomatonApiClient(server, apiKey);

        var app = await ResolveAppAsync(client, appIdentifier);
        var appId = GetRequiredString(app, "id");
        var appName = GetRequiredString(app, "name");
        var slug = TryReadString(app, "publicUrl");
        var folderName = SanitizeDirectoryName(slug ?? appName);
        var targetDir = dir ?? Path.Combine(cwd, folderName);

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            throw new InvalidOperationException($"Target directory already exists and is not empty: {targetDir}");
        }

        Directory.CreateDirectory(targetDir);
        WriteStart(nonInteractive, appName, targetDir);

        var files = await ListSourceFilesAsync(client, appId);
        var sourceFiles = files.Where(IsPortableSourceFile).ToList();
        if (sourceFiles.Count == 0)
        {
            throw new InvalidOperationException("This app does not have portable source files to pull. Runtime frontend fallback is not enabled.");
        }

        await DownloadFilesAsync(client, appId, sourceFiles, targetDir, nonInteractive);

        var hasFrontend = sourceFiles.Any(path => path.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase));
        var hasBackend = sourceFiles.Any(path => path.StartsWith("backend/", StringComparison.OrdinalIgnoreCase));
        var isBuildBasedFrontend = hasFrontend && File.Exists(Path.Combine(targetDir, "frontend", "package.json"));

        config.SaveProjectConfig(targetDir, new AutomatonConfig
        {
            AppId = appId,
            Name = appName,
            Slug = slug,
            Server = server,
            Frontend = hasFrontend
                ? new FrontendConfig
                {
                    Build = isBuildBasedFrontend ? FrontendBuildCommand : null,
                    DeployDir = isBuildBasedFrontend ? "frontend/dist" : "frontend"
                }
                : null,
            Backend = hasBackend
                ? new BackendConfig
                {
                    DeployDir = "backend"
                }
                : null
        });

        var guidance = new AiGuidancePackService().Sync(targetDir);

        if (nonInteractive)
        {
            Console.WriteLine($"Wrote automaton.json ({guidance.Mode})");
            Console.WriteLine($"Linked app: {appId}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Wrote [bold]automaton.json[/] and linked app [cyan]{appId}[/]");
            AnsiConsole.MarkupLine($"[grey]Local AI guidance synced ({guidance.Mode}).[/]");
        }
    }

    private static async Task<List<string>> ListSourceFilesAsync(AutomatonApiClient client, string appId)
    {
        var filesResult = await client.ListScopedFilesAsync(appId, "source");
        var files = new List<string>();

        if (filesResult.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in filesResult.EnumerateArray())
            {
                var value = f.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    files.Add(value);
                }
            }
        }
        else if (filesResult.TryGetProperty("files", out var filesProperty))
        {
            foreach (var f in filesProperty.EnumerateArray())
            {
                var value = f.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    files.Add(value);
                }
            }
        }

        return files
            .Where(f => !f.Equals("automaton.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static async Task DownloadFilesAsync(
        AutomatonApiClient client,
        string appId,
        IReadOnlyList<string> files,
        string targetDir,
        bool nonInteractive)
    {
        var written = 0;

        if (nonInteractive)
        {
            foreach (var filePath in files)
            {
                await DownloadFileAsync(client, appId, targetDir, filePath);
                written++;
            }

            Console.WriteLine($"Downloaded {written} file(s)");
            return;
        }

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Downloading files[/]", maxValue: files.Count);
                foreach (var filePath in files)
                {
                    await DownloadFileAsync(client, appId, targetDir, filePath);
                    written++;
                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Downloaded [bold]{written}[/] file(s)");
    }

    private static async Task DownloadFileAsync(AutomatonApiClient client, string appId, string targetDir, string filePath)
    {
        var content = await client.ReadScopedFileAsync(appId, "source", filePath);
        var localPath = Path.Combine(targetDir, filePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, content, Encoding.UTF8);
    }

    private static List<string> GetOverwritePaths(IEnumerable<string> files, string targetDir)
    {
        var overwritePaths = new List<string>();

        foreach (var filePath in files)
        {
            var localPath = Path.Combine(targetDir, filePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
            {
                overwritePaths.Add(filePath);
            }
        }

        return overwritePaths;
    }

    private static void EnsureOverwriteApproved(IReadOnlyList<string> overwritePaths, bool force, bool nonInteractive)
    {
        if (overwritePaths.Count == 0)
        {
            return;
        }

        if (force)
        {
            WriteOverwriteNotice(overwritePaths, nonInteractive, forced: true);
            return;
        }

        if (nonInteractive)
        {
            throw new InvalidOperationException(BuildNonInteractiveOverwriteError(overwritePaths));
        }

        WriteOverwriteNotice(overwritePaths, nonInteractive, forced: false);
        Console.Write("Continue? [y/N] ");
        var confirmation = Console.ReadLine()?.Trim();
        if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationCanceledException("Pull cancelled.");
        }
    }

    private static void WriteOverwriteNotice(IReadOnlyList<string> overwritePaths, bool nonInteractive, bool forced)
    {
        var message = BuildOverwriteMessage(overwritePaths, forced);

        if (nonInteractive)
        {
            Console.WriteLine(message);
            return;
        }

        foreach (var line in message.Split(Environment.NewLine))
        {
            AnsiConsole.MarkupLine(Markup.Escape(line));
        }
    }

    private static string BuildNonInteractiveOverwriteError(IReadOnlyList<string> overwritePaths)
    {
        var preview = BuildOverwritePreview(overwritePaths);
        return $"Pull would overwrite {overwritePaths.Count} existing local file(s). Re-run with --force to continue.{Environment.NewLine}{preview}";
    }

    private static string BuildOverwriteMessage(IReadOnlyList<string> overwritePaths, bool forced)
    {
        var intro = forced
            ? $"Warning: --force enabled. Pull will overwrite {overwritePaths.Count} existing local file(s) from the remote app."
            : $"Warning: pull will overwrite {overwritePaths.Count} existing local file(s) from the remote app.";

        return $"{intro}{Environment.NewLine}{BuildOverwritePreview(overwritePaths)}{Environment.NewLine}Local-only files will be kept.";
    }

    private static string BuildOverwritePreview(IReadOnlyList<string> overwritePaths)
    {
        var lines = overwritePaths
            .Take(OverwritePreviewCount)
            .Select(path => $"  {path}")
            .ToList();

        if (overwritePaths.Count > OverwritePreviewCount)
        {
            lines.Add($"  ... and {overwritePaths.Count - OverwritePreviewCount} more");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static async Task<JsonElement> ResolveAppAsync(AutomatonApiClient client, string appIdentifier)
    {
        if (Guid.TryParse(appIdentifier, out _))
        {
            return await client.GetAppAsync(appIdentifier);
        }

        var apps = await client.ListAppsAsync();
        if (apps.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Unexpected app list response while resolving the app slug.");
        }

        foreach (var app in apps.EnumerateArray())
        {
            var publicUrl = TryReadString(app, "publicUrl");
            if (string.Equals(publicUrl, appIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return app.Clone();
            }
        }

        throw new InvalidOperationException($"No accessible app matched slug '{appIdentifier}'.");
    }

    private static bool IsPortableSourceFile(string relativePath)
    {
        return relativePath.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith("backend/", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeDirectoryName(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), @"[^\w\- ]+", string.Empty)
            .Trim()
            .Replace(' ', '-');
        sanitized = Regex.Replace(sanitized, @"\-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "automaton-app" : sanitized;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = TryReadString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"App response did not include '{propertyName}'.");
        }

        return value;
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static void WriteStart(bool nonInteractive, string appName, string targetDir)
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

    private static void WriteNoFiles(bool nonInteractive)
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
}
