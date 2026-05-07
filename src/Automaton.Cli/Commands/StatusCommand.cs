using Automaton.Cli.Models;
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
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.TryLoadProjectConfig(cwd);
                var (server, apiKey) = config.ResolveCredentials(appConfig, cwd);
                var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, nextSteps);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
                checks.Add(appLinkCheck);
                checks.Add(credentialsCheck);

                if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Status failed", Checks = checks, NextSteps = nextSteps.ToList() };
                    if (asJson)
                    {
                        CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                    }
                    else
                    {
                        CliDiagnosticsWriter.Write(failure);
                    }
                    Environment.ExitCode = 1;
                    return;
                }

                using var client = new AutomatonApiClient(server!, apiKey!);
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
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, true, $"remote app loaded: {appConfig.AppId}"));
                var statusResult = new CommandDiagnosticResult { Success = true, Summary = "Status loaded", Checks = checks, NextSteps = [] };

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = true,
                        summary = statusResult.Summary,
                        checks = statusResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                        nextSteps = statusResult.NextSteps,
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
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Status failed", Checks = checks, NextSteps = nextSteps.ToList() };
                if (asJson)
                {
                    CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                }
                else
                {
                    CliDiagnosticsWriter.Write(failure);
                }
                Environment.ExitCode = 1;
            }
        }, jsonOption);

        return cmd;
    }
}