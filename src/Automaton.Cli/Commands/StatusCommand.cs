using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class StatusCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("status", "Show the current linked app, deploy settings, and publish state")
        {
            jsonOption
        };

        cmd.SetHandler(async (bool asJson) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.LoadProjectConfig(cwd);
                var (server, apiKey) = config.RequireCredentials(appConfig, cwd);
                using var client = new AutomatonApiClient(server, apiKey);
                var app = await client.GetAppAsync(appConfig.AppId);

                var deployDirectory = ZipService.ResolveDeployDirectory(cwd, appConfig.DeployDir);
                var backendScripts = Directory.Exists(Path.Combine(cwd, "backend"))
                    ? Directory.GetFiles(Path.Combine(cwd, "backend"), "*.py", SearchOption.AllDirectories).Length
                    : 0;

                var executionRunnerId = app.TryGetProperty("executionRunnerId", out var runnerProperty)
                    && runnerProperty.ValueKind == JsonValueKind.String
                        ? runnerProperty.GetString()
                        : null;
                var publicUrl = app.TryGetProperty("publicUrl", out var publicUrlProperty) ? publicUrlProperty.GetString() : null;
                var isPublished = app.TryGetProperty("isPublished", out var publishedProperty) && publishedProperty.GetBoolean();
                var liveUrl = isPublished && !string.IsNullOrWhiteSpace(publicUrl) ? $"{server}/web/{publicUrl}" : null;

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = true,
                        project = new
                        {
                            root = cwd,
                            configPath = Path.Combine(cwd, "automaton.json"),
                            appId = appConfig.AppId,
                            name = appConfig.Name,
                            server,
                            build = appConfig.Build,
                            deployDir = appConfig.DeployDir,
                            resolvedDeployDir = deployDirectory,
                            backendScripts
                        },
                        remote = new
                        {
                            isPublished,
                            publicUrl,
                            liveUrl,
                            executionRunnerId,
                            version = app.TryGetProperty("version", out var versionProperty) ? versionProperty.GetInt32() : 0
                        }
                    });
                    return;
                }

                var table = new Table();
                table.AddColumn("Field");
                table.AddColumn("Value");
                table.AddRow("app", $"{appConfig.Name} ({appConfig.AppId})");
                table.AddRow("server", server);
                table.AddRow("build", string.IsNullOrWhiteSpace(appConfig.Build) ? "-" : appConfig.Build);
                table.AddRow("deploy dir", Path.GetRelativePath(cwd, deployDirectory));
                table.AddRow("backend scripts", backendScripts.ToString());
                table.AddRow("runner", string.IsNullOrWhiteSpace(executionRunnerId) ? "-" : executionRunnerId);
                table.AddRow("published", isPublished ? "yes" : "no");
                table.AddRow("public url", liveUrl ?? "-");
                AnsiConsole.Write(table);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, jsonOption);

        return cmd;
    }
}