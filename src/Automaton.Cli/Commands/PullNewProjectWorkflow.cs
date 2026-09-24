using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class PullNewProjectWorkflow
{    public static async Task PullAsync(ConfigService config, string cwd, string appIdentifier, string? dir, bool nonInteractive, string? environmentName)
    {
        var (server, apiKey) = config.RequireCredentials(
            directory: cwd,
            environmentName: environmentName,
            allowMissingEnvironment: true);
        using var client = new AutomatonApiClient(server, apiKey);

        var app = await PullCommand.ResolveAppAsync(client, appIdentifier);
        var appId = PullCommand.GetRequiredString(app, "id");
        var appName = PullCommand.GetRequiredString(app, "name");
        var application = PullCommand.ReadApplicationContract(app);
        var slug = PullCommand.TryReadString(app, "publicUrl");
        var folderName = PullCommand.SanitizeDirectoryName(slug ?? appName);
        var targetDir = dir ?? Path.Combine(cwd, folderName);

        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any())
        {
            throw new InvalidOperationException($"Target directory already exists and is not empty: {targetDir}");
        }

        var sourceFiles = await PullCommand.ListSourceFilesAsync(client, appId);
        if (sourceFiles.Count == 0)
        {
            throw new InvalidOperationException("This app does not have portable source files to pull. Runtime artifact fallback is not enabled.");
        }

        var downloads = PullCommand.PrepareDownloads(sourceFiles, targetDir);
        PullCommandOutput.WriteStart(nonInteractive, appName, targetDir);

        var targetExisted = Directory.Exists(targetDir);
        var stagingDirectory = PullCommand.CreateStagingDirectory(targetDir);
        try
        {
            var stagedDownloads = downloads
                .Select(download => download with
                {
                    LocalPath = Path.Combine(stagingDirectory, Path.GetRelativePath(targetDir, download.LocalPath))
                })
                .ToList();

            await PullCommand.DownloadFilesAsync(client, appId, stagedDownloads, nonInteractive);

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

            PullCommand.CommitStagedProject(stagingDirectory, targetDir, targetExisted);

            PullCommandOutput.WritePulledApp(nonInteractive, appName, appId, targetDir);
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
}
