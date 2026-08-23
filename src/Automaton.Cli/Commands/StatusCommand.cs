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
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");

        var cmd = new Command("status", "Show the current linked app, deploy settings, and publish state")
        {
            jsonOption,
            environmentOption
        };

        cmd.SetHandler(async (bool asJson, string? environmentName) =>
        {
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.TryLoadProjectConfig(cwd);
                var resolved = config.ResolveContext(appConfig, cwd, environmentName);
                var server = resolved.Server;
                var apiKey = resolved.ApiKey;
                var appId = resolved.AppId;
                var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, appId, nextSteps);
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
                var app = await client.GetAppAsync(appId!);
                var targets = await client.ListDeploymentTargetsAsync(appId!);
                var defaultTarget = targets.SingleOrDefault(target => target.IsDefault);

                var artifactDirectory = !string.IsNullOrWhiteSpace(appConfig.Application.Build.OutputDirectory)
                    ? ZipService.ResolveDeployDirectory(cwd, appConfig.Application.Build.OutputDirectory)
                    : null;

                var publicUrl = app.TryGetProperty("publicUrl", out var publicUrlProperty) ? publicUrlProperty.GetString() : null;
                var isPublished = app.TryGetProperty("isPublished", out var publishedProperty) && publishedProperty.GetBoolean();
                var liveUrl = isPublished && !string.IsNullOrWhiteSpace(publicUrl) ? CliAppUrlBuilder.BuildPublishedUrl(server!, publicUrl) : null;
                var openAppUrl = CliAppUrlBuilder.BuildRestrictedAppUrl(server!, appId);
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, true, $"remote app loaded: {appId}"));
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
                            appId = appId,
                            name = appConfig.Name,
                            environment = resolved.EnvironmentName,
                            server,
                            application = appConfig.Application,
                            resolvedArtifactDirectory = artifactDirectory
                        },
                        remote = new
                        {
                            isPublished,
                            publicUrl,
                            openAppUrl,
                            liveUrl,
                            defaultTarget,
                            targets,
                            version = app.TryGetProperty("version", out var versionProperty) ? versionProperty.GetInt32() : 0
                        }
                    });
                    return;
                }

                var table = new Table();
                table.AddColumn("Field");
                table.AddColumn("Value");
                table.AddRow("app", $"{appConfig.Name} ({appId})");
                table.AddRow("environment", string.IsNullOrWhiteSpace(resolved.EnvironmentName) ? "-" : resolved.EnvironmentName);
                table.AddRow("server", server);
                table.AddRow("build executable", string.IsNullOrWhiteSpace(appConfig.Application.Build.Command?.Executable) ? "-" : appConfig.Application.Build.Command.Executable);
                table.AddRow("runtime executable", string.IsNullOrWhiteSpace(appConfig.Application.Runtime.Command?.Executable) ? "-" : appConfig.Application.Runtime.Command.Executable);
                table.AddRow("artifact directory", artifactDirectory == null ? "-" : Path.GetRelativePath(cwd, artifactDirectory));
                table.AddRow(
                    "Live target",
                    defaultTarget == null
                        ? "-"
                        : $"{defaultTarget.Slug} → {defaultTarget.Runner?.Name ?? "unassigned"}");
                table.AddRow("targets", targets.Count.ToString());
                table.AddRow("open app", string.IsNullOrWhiteSpace(openAppUrl) ? "-" : openAppUrl);
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
        }, jsonOption, environmentOption);

        return cmd;
    }

}
