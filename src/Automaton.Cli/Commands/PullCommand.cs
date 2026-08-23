using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;
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
    private const int OverwritePreviewCount = 10;
    internal sealed record SourceDownload(string RemotePath, string LocalPath);

    public static Command Build(ConfigService config)
    {
        var appArgument = new Argument<string?>("app", () => null, "App ID or slug to pull into a new local folder");
        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Target directory (defaults to current directory for refresh mode, or a new subfolder in the current directory for app mode)");
        var forceOption = new Option<bool>("--force", "Overwrite matching local files without prompting");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable progress rendering and use plain output");
        var environmentOption = new Option<string?>("--env", "Environment name to use and retain in a newly pulled project");

        var cmd = new Command("pull", "Download editable app source files from the server")
        {
            appArgument,
            dirOption,
            forceOption,
            nonInteractiveOption,
            environmentOption
        };

        cmd.SetHandler(async (string? app, string? dir, bool force, bool nonInteractiveFlag, string? environmentName) =>
        {
            var nonInteractive = CliInteraction.IsNonInteractive(nonInteractiveFlag);

            try
            {
                var cwd = Directory.GetCurrentDirectory();

                if (string.IsNullOrWhiteSpace(app))
                {
                    await PullIntoLinkedProjectAsync(config, cwd, dir, force, nonInteractive, environmentName);
                    return;
                }

                await PullIntoNewProjectAsync(config, cwd, app.Trim(), dir, nonInteractive, environmentName);
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
        }, appArgument, dirOption, forceOption, nonInteractiveOption, environmentOption);

        return cmd;
    }

    private static async Task PullIntoLinkedProjectAsync(ConfigService config, string cwd, string? dir, bool force, bool nonInteractive, string? environmentName)
    {
        var appConfig = config.LoadProjectConfig(cwd);
        var resolved = config.ResolveContext(appConfig, cwd, environmentName);
        if (string.IsNullOrWhiteSpace(resolved.AppId))
        {
            throw new InvalidOperationException("No appId is configured for the selected environment.");
        }

        var (server, apiKey) = config.RequireCredentials(appConfig, cwd, environmentName);
        var targetDir = dir ?? cwd;

        WriteStart(nonInteractive, appConfig.Name, targetDir);

        using var client = new AutomatonApiClient(server, apiKey);
        var sourceFiles = await ListSourceFilesAsync(client, resolved.AppId);

        if (sourceFiles.Count == 0)
        {
            WriteNoFiles(nonInteractive);
            return;
        }

        var downloads = PrepareDownloads(sourceFiles, targetDir);
        var overwritePaths = GetOverwritePaths(downloads);
        EnsureOverwriteApproved(overwritePaths, force, nonInteractive);

        await DownloadFilesAsync(client, resolved.AppId, downloads, nonInteractive);
    }

    private static async Task PullIntoNewProjectAsync(ConfigService config, string cwd, string appIdentifier, string? dir, bool nonInteractive, string? environmentName)
    {
        var resolved = config.ResolveContext(directory: cwd, environmentName: environmentName);
        var (server, apiKey) = config.RequireCredentials(directory: cwd, environmentName: environmentName);
        using var client = new AutomatonApiClient(server, apiKey);

        var app = await ResolveAppAsync(client, appIdentifier);
        var appId = GetRequiredString(app, "id");
        var appName = GetRequiredString(app, "name");
        var application = ReadApplicationContract(app);
        var slug = TryReadString(app, "publicUrl");
        var folderName = SanitizeDirectoryName(slug ?? appName);
        var targetDir = dir ?? Path.Combine(cwd, folderName);

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            throw new InvalidOperationException($"Target directory already exists and is not empty: {targetDir}");
        }

        var sourceFiles = await ListSourceFilesAsync(client, appId);
        if (sourceFiles.Count == 0)
        {
            throw new InvalidOperationException("This app does not have portable source files to pull. Runtime frontend fallback is not enabled.");
        }

        var downloads = PrepareDownloads(sourceFiles, targetDir);
        Directory.CreateDirectory(targetDir);
        WriteStart(nonInteractive, appName, targetDir);

        await DownloadFilesAsync(client, appId, downloads, nonInteractive);

        var pulledConfig = config.TryLoadProjectConfig(targetDir);
        if (pulledConfig == null)
        {
            pulledConfig = new AutomatonConfig
            {
                Name = appName,
                Slug = slug,
                Application = application
            };
        }
        else
        {
            pulledConfig.Name = appName;
            pulledConfig.Slug = slug;
            pulledConfig.Application = application;
        }

        config.SaveProjectConfig(targetDir, pulledConfig);
        var selectedEnvironmentName = resolved.EnvironmentName ?? ConfigService.DefaultEnvironmentName;
        var environment = config.LoadNamedEnvironmentConfig(targetDir, selectedEnvironmentName) ?? new GlobalConfig();
        environment.Server = server;
        environment.ApiKey = apiKey;
        environment.AppId = appId;
        config.SaveNamedEnvironmentConfig(targetDir, selectedEnvironmentName, environment);
        config.SaveProjectEnvironmentSelection(
            targetDir,
            new ProjectEnvironmentSelection { Default = selectedEnvironmentName });

        string? guidanceWarning = null;
        AiGuidanceSyncResult? guidance = null;
        try
        {
            var guidanceReferences = await client.GetAiGuidanceExportAsync();
            guidance = new AiGuidancePackService().Sync(targetDir, guidanceReferences);
        }
        catch (Exception ex)
        {
            guidanceWarning = $"Latest AI guidance unavailable: {ex.Message}";
        }

        if (nonInteractive)
        {
            Console.WriteLine(guidance is null ? "Wrote automaton.json" : $"Wrote automaton.json ({guidance.Mode})");
            Console.WriteLine($"Linked app: {appId}");
            if (!string.IsNullOrWhiteSpace(guidanceWarning))
            {
                Console.WriteLine($"Warning: {guidanceWarning}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Wrote [bold]automaton.json[/] and linked app [cyan]{appId}[/]");
            if (guidance is not null)
            {
                AnsiConsole.MarkupLine($"[grey]Local AI guidance synced ({guidance.Mode}).[/]");
            }
            else if (!string.IsNullOrWhiteSpace(guidanceWarning))
            {
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(guidanceWarning)}");
            }
        }
    }

    private static ApplicationContract ReadApplicationContract(JsonElement app)
    {
        if (!app.TryGetProperty("application", out var application)
            || application.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "The remote app does not declare the required generic application contract.");
        }

        return ApplicationContractSerialization.Deserialize(application.GetRawText());
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

        return SelectSourceFiles(files);
    }

    internal static List<string> SelectSourceFiles(IEnumerable<string> files)
    {
        return files
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(file => file.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static async Task DownloadFilesAsync(
        AutomatonApiClient client,
        string appId,
        IReadOnlyList<SourceDownload> downloads,
        bool nonInteractive)
    {
        var written = 0;

        if (nonInteractive)
        {
            foreach (var download in downloads)
            {
                await DownloadFileAsync(client, appId, download);
                written++;
            }

            Console.WriteLine($"Downloaded {written} file(s)");
            return;
        }

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[cyan]Downloading files[/]", maxValue: downloads.Count);
                foreach (var download in downloads)
                {
                    await DownloadFileAsync(client, appId, download);
                    written++;
                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Downloaded [bold]{written}[/] file(s)");
    }

    private static async Task DownloadFileAsync(AutomatonApiClient client, string appId, SourceDownload download)
    {
        var content = await client.ReadScopedFileAsync(appId, "source", download.RemotePath);
        Directory.CreateDirectory(Path.GetDirectoryName(download.LocalPath)!);
        await File.WriteAllTextAsync(download.LocalPath, content, Encoding.UTF8);
    }

    private static List<string> GetOverwritePaths(IEnumerable<SourceDownload> downloads)
    {
        var overwritePaths = new List<string>();

        foreach (var download in downloads)
        {
            if (File.Exists(download.LocalPath))
            {
                overwritePaths.Add(download.RemotePath);
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

    internal static string ResolveTargetPath(string targetDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("Remote source contained an empty file path.");
        }

        var normalizedRelativePath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedRelativePath))
        {
            throw new InvalidOperationException($"Remote source path must be project-relative: {relativePath}");
        }

        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(fullTargetDirectory, normalizedRelativePath));
        var relative = Path.GetRelativePath(fullTargetDirectory, fullPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Remote source path points outside the target directory: {relativePath}");
        }

        return fullPath;
    }

    internal static List<SourceDownload> PrepareDownloads(IEnumerable<string> files, string targetDirectory)
    {
        var downloads = files
            .Select(remotePath => new SourceDownload(remotePath, ResolveTargetPath(targetDirectory, remotePath)))
            .ToList();

        foreach (var download in downloads)
        {
            EnsureNoReparsePoints(download.LocalPath);
        }

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            var collision = downloads
                .GroupBy(download => download.LocalPath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (collision is not null)
            {
                throw new InvalidOperationException(
                    $"Remote source paths collide on this case-insensitive filesystem: {string.Join(", ", collision.Select(download => download.RemotePath))}");
            }
        }

        return downloads;
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException(
                        $"Remote source path cannot be written through a symbolic link or junction: {path}");
                }
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }
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
