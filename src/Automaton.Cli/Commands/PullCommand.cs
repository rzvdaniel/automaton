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
        var environmentOption = new Option<string?>("--env", "Local project profile to link, for example local or live");

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
        var targetDir = dir ?? config.FindNearestProjectRoot(cwd) ?? cwd;

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

        await StageAndCommitRefreshAsync(
            targetDir,
            downloads,
            remotePath => client.ReadScopedFileAsync(resolved.AppId, "source", remotePath),
            nonInteractive);

        WritePulledApp(nonInteractive, appConfig.Name, resolved.AppId, targetDir);
    }

    private static async Task PullIntoNewProjectAsync(ConfigService config, string cwd, string appIdentifier, string? dir, bool nonInteractive, string? environmentName)
    {
        var (server, apiKey) = config.RequireCredentials(
            directory: cwd,
            environmentName: environmentName,
            allowMissingEnvironment: true);
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
            throw new InvalidOperationException("This app does not have portable source files to pull. Runtime artifact fallback is not enabled.");
        }

        var downloads = PrepareDownloads(sourceFiles, targetDir);
        WriteStart(nonInteractive, appName, targetDir);

        var targetExisted = Directory.Exists(targetDir);
        var stagingDirectory = CreateStagingDirectory(targetDir);
        try
        {
            var stagedDownloads = downloads
                .Select(download => download with
                {
                    LocalPath = Path.Combine(stagingDirectory, Path.GetRelativePath(targetDir, download.LocalPath))
                })
                .ToList();

            await DownloadFilesAsync(client, appId, stagedDownloads, nonInteractive);

            var pulledConfig = config.TryLoadProjectConfig(stagingDirectory);
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

            config.SaveProjectConfig(stagingDirectory, pulledConfig);
            var selectedEnvironmentName = config.ResolveAppBindingEnvironmentName(cwd, environmentName);
            var environment = config.LoadNamedEnvironmentConfig(stagingDirectory, selectedEnvironmentName) ?? new GlobalConfig();
            environment.Server = server;
            environment.AppId = appId;
            config.SaveNamedEnvironmentConfig(stagingDirectory, selectedEnvironmentName, environment);
            config.SaveProjectEnvironmentSelection(
                stagingDirectory,
                new ProjectEnvironmentSelection { Default = selectedEnvironmentName });

            string? guidanceWarning = null;
            AiGuidanceSyncResult? guidance = null;
            try
            {
                var guidanceReferences = await client.GetAiGuidanceExportAsync();
                guidance = new AiGuidancePackService().Sync(stagingDirectory, guidanceReferences);
            }
            catch (Exception ex)
            {
                guidanceWarning = $"Latest AI guidance unavailable: {ex.Message}";
            }

            CommitStagedProject(stagingDirectory, targetDir, targetExisted);

            WritePulledApp(nonInteractive, appName, appId, targetDir);
            if (guidance is not null)
            {
                if (nonInteractive) Console.WriteLine($"Local AI guidance synced ({guidance.Mode}).");
                else AnsiConsole.MarkupLine($"[grey]Local AI guidance synced ({guidance.Mode}).[/]");
            }
            else if (!string.IsNullOrWhiteSpace(guidanceWarning))
            {
                if (nonInteractive) Console.WriteLine($"Warning: {guidanceWarning}");
                else AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(guidanceWarning)}");
            }
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    internal static string CreateStagingDirectory(string targetDirectory)
    {
        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        var parent = Directory.GetParent(fullTargetDirectory)?.FullName
            ?? throw new InvalidOperationException("The pull target must have a parent directory.");
        Directory.CreateDirectory(parent);

        var stagingDirectory = Path.Combine(parent, $".{Path.GetFileName(fullTargetDirectory)}.pulling-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        return stagingDirectory;
    }

    internal static void CommitStagedProject(string stagingDirectory, string targetDirectory, bool targetExisted)
    {
        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        if (!targetExisted)
        {
            Directory.Move(stagingDirectory, fullTargetDirectory);
            return;
        }

        if (!Directory.Exists(fullTargetDirectory))
        {
            throw new InvalidOperationException("The pull target changed before commit.");
        }

        if (Directory.EnumerateFileSystemEntries(fullTargetDirectory).Any())
        {
            throw new InvalidOperationException("The pull target must remain empty until commit.");
        }

        Directory.Delete(fullTargetDirectory);
        Directory.Move(stagingDirectory, fullTargetDirectory);
    }

    internal static void CommitStagedRefresh(
        string stagingDirectory,
        string targetDirectory,
        IReadOnlyList<SourceDownload> downloads)
    {
        var fullTargetDirectory = Path.GetFullPath(targetDirectory);
        var backupDirectory = stagingDirectory + ".rollback";
        var changedFiles = new List<(string Target, string? Backup)>();
        var preserveBackups = false;

        try
        {
            foreach (var download in downloads)
            {
                var relativePath = Path.GetRelativePath(fullTargetDirectory, download.LocalPath);
                var stagedPath = ResolveTargetPath(stagingDirectory, relativePath);
                if (!File.Exists(stagedPath))
                {
                    throw new InvalidOperationException($"Staged pull file is missing: {download.RemotePath}");
                }

                EnsureNoReparsePoints(download.LocalPath);
                Directory.CreateDirectory(Path.GetDirectoryName(download.LocalPath)!);

                string? backupPath = null;
                if (File.Exists(download.LocalPath))
                {
                    backupPath = ResolveTargetPath(backupDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(download.LocalPath, backupPath);
                }

                changedFiles.Add((download.LocalPath, backupPath));
                File.Move(stagedPath, download.LocalPath, overwrite: true);
            }
        }
        catch (Exception commitError)
        {
            Exception? rollbackError = null;
            foreach (var changedFile in changedFiles.AsEnumerable().Reverse())
            {
                try
                {
                    if (changedFile.Backup is null)
                    {
                        if (File.Exists(changedFile.Target))
                        {
                            File.Delete(changedFile.Target);
                        }
                    }
                    else
                    {
                        File.Copy(changedFile.Backup, changedFile.Target, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    rollbackError ??= ex;
                }
            }

            if (rollbackError is not null)
            {
                preserveBackups = true;
                throw new InvalidOperationException(
                    $"Pull could not finish and local rollback also failed: {rollbackError.Message}. Recovery copies are in '{backupDirectory}'.",
                    commitError);
            }

            throw;
        }
        finally
        {
            if (!preserveBackups && Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }
        }
    }

    internal static async Task StageAndCommitRefreshAsync(
        string targetDirectory,
        IReadOnlyList<SourceDownload> downloads,
        Func<string, Task<string>> readFile,
        bool nonInteractive)
    {
        var stagingDirectory = CreateStagingDirectory(targetDirectory);
        try
        {
            var stagedDownloads = downloads
                .Select(download => download with
                {
                    LocalPath = ResolveTargetPath(
                        stagingDirectory,
                        Path.GetRelativePath(targetDirectory, download.LocalPath))
                })
                .ToList();

            await DownloadFilesAsync(stagedDownloads, readFile, nonInteractive);
            CommitStagedRefresh(stagingDirectory, targetDirectory, downloads);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
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
        await DownloadFilesAsync(
            downloads,
            remotePath => client.ReadScopedFileAsync(appId, "source", remotePath),
            nonInteractive);
    }

    internal static async Task DownloadFilesAsync(
        IReadOnlyList<SourceDownload> downloads,
        Func<string, Task<string>> readFile,
        bool nonInteractive)
    {
        var written = 0;

        if (nonInteractive)
        {
            foreach (var download in downloads)
            {
                await DownloadFileAsync(download, readFile);
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
                    await DownloadFileAsync(download, readFile);
                    written++;
                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Downloaded [bold]{written}[/] file(s)");
    }

    private static async Task DownloadFileAsync(SourceDownload download, Func<string, Task<string>> readFile)
    {
        var content = await readFile(download.RemotePath);
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

    internal static async Task<JsonElement> ResolveAppAsync(AutomatonApiClient client, string appIdentifier)
    {
        return await client.GetAppByIdOrSlugAsync(appIdentifier);
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

    private static void WritePulledApp(bool nonInteractive, string appName, string appId, string targetDirectory)
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
