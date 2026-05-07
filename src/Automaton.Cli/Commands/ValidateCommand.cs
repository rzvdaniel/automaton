using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class ValidateCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("validate", "Validate the current project before deploy or publish")
        {
            jsonOption
        };

        cmd.SetHandler(async (bool asJson) =>
        {
            var checks = new List<object>();

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var projectConfig = config.TryLoadProjectConfig(cwd);
                var (resolvedServer, resolvedApiKey) = config.ResolveCredentials(projectConfig, cwd);

                AddCheck(checks, "automatonJson", projectConfig != null && !string.IsNullOrWhiteSpace(projectConfig.AppId), projectConfig != null ? "automaton.json found" : "automaton.json missing");
                AddCheck(checks, "credentials", !string.IsNullOrWhiteSpace(resolvedServer) && !string.IsNullOrWhiteSpace(resolvedApiKey),
                    !string.IsNullOrWhiteSpace(resolvedServer) && !string.IsNullOrWhiteSpace(resolvedApiKey)
                        ? "credentials resolved"
                        : "missing server or API key");

                var backendScripts = Directory.Exists(Path.Combine(cwd, "backend"))
                    ? Directory.GetFiles(Path.Combine(cwd, "backend"), "*.py", SearchOption.AllDirectories).Length
                    : 0;
                AddCheck(checks, "backendScripts", true, backendScripts == 0 ? "no backend scripts detected" : $"{backendScripts} backend script(s) detected");

                if (projectConfig != null)
                {
                    try
                    {
                        var deployDirectory = ZipService.ResolveDeployDirectory(cwd, projectConfig.DeployDir);
                        AddCheck(checks, "deployDirectory", Directory.Exists(deployDirectory), $"resolved deploy directory: {Path.GetRelativePath(cwd, deployDirectory)}");
                    }
                    catch (Exception ex)
                    {
                        AddCheck(checks, "deployDirectory", false, ex.Message);
                    }
                }

                if (projectConfig != null && !string.IsNullOrWhiteSpace(resolvedServer) && !string.IsNullOrWhiteSpace(resolvedApiKey))
                {
                    using var client = new AutomatonApiClient(resolvedServer!, resolvedApiKey!);
                    var app = await client.GetAppAsync(projectConfig.AppId);
                    AddCheck(checks, "remoteApp", true, $"remote app found: {projectConfig.Name}");

                    var runnerId = app.TryGetProperty("executionRunnerId", out var runnerProperty) && runnerProperty.ValueKind == JsonValueKind.String
                        ? runnerProperty.GetString()
                        : null;
                    AddCheck(checks, "runnerAssignment", backendScripts == 0 || !string.IsNullOrWhiteSpace(runnerId),
                        backendScripts == 0
                            ? "runner not required because no backend scripts were found"
                            : string.IsNullOrWhiteSpace(runnerId)
                                ? "backend scripts detected but no runner is assigned"
                                : $"runner assigned: {runnerId}");
                }

                var success = checks.All(check => (bool)(check.GetType().GetProperty("success")?.GetValue(check) ?? false));

                if (asJson)
                {
                    CliJson.Write(new { success, checks });
                    return;
                }

                var table = new Table();
                table.AddColumn("Check");
                table.AddColumn("Result");
                table.AddColumn("Details");

                foreach (var check in checks)
                {
                    var name = check.GetType().GetProperty("name")?.GetValue(check)?.ToString() ?? "?";
                    var passed = (bool)(check.GetType().GetProperty("success")?.GetValue(check) ?? false);
                    var detail = check.GetType().GetProperty("message")?.GetValue(check)?.ToString() ?? string.Empty;
                    table.AddRow(name, passed ? "[green]pass[/]" : "[red]fail[/]", detail);
                }

                AnsiConsole.Write(table);
                if (!success)
                {
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, jsonOption);

        return cmd;
    }

    private static void AddCheck(ICollection<object> checks, string name, bool success, string message)
    {
        checks.Add(new { name, success, message });
    }
}