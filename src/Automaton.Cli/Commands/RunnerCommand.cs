using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class RunnerCommand
{
    public static Command Build(ConfigService config)
    {
        var runnerService = new RunnerInstallService(config);

        var root = new Command("runner", "Install and manage a local Automaton runner");
        root.AddCommand(BuildInstallCommand(config, runnerService));
        root.AddCommand(BuildStatusCommand(runnerService));
        root.AddCommand(BuildLogsCommand(runnerService));
        root.AddCommand(BuildStopCommand(runnerService));
        root.AddCommand(BuildRestartCommand(runnerService));
        root.AddCommand(BuildUninstallCommand(runnerService));

        root.AddAlias("r");
        return root;
    }

    private static Command BuildInstallCommand(ConfigService config, RunnerInstallService runnerService)
    {
        var nameOption = new Option<string?>(["--name", "-n"], "Runner name");
        var serverOption = new Option<string?>("--server", "Automaton server URL");
        var keyOption = new Option<string?>("--key", "Runner API key");
        var nativeOption = new Option<bool>("--native", "Install the runner as a native service instead of the default Docker container");
        var replaceOption = new Option<bool>("--replace", "Replace any existing local runner install with the same name");
        var autoStartOption = new Option<bool?>("--auto-start", "Start automatically on boot / Docker restart");
        var imageOption = new Option<string?>("--image", "Docker runner image override");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("install", "Install a local Automaton runner")
        {
            nameOption, serverOption, keyOption, nativeOption, replaceOption, autoStartOption, imageOption, jsonOption
        };

        cmd.SetHandler((string? name, string? server, string? key, bool native, bool replace, bool? autoStart, string? image, bool asJson) =>
            RunnerInstallCommandHandler.HandleAsync(
                config, runnerService, name, server, key, native, replace, autoStart, image, asJson),
            nameOption, serverOption, keyOption, nativeOption, replaceOption, autoStartOption, imageOption, jsonOption);

        cmd.AddAlias("up");

        return cmd;
    }

    private static Command BuildStatusCommand(RunnerInstallService runnerService)
    {
        var nameOption = new Option<string?>("--name", "Runner name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var cmd = new Command("status", "Show local runner status")
        {
            nameOption, jsonOption
        };

        cmd.SetHandler((string? name, bool asJson) =>
        {
            var result = runnerService.GetInstallStatus(name);
            WriteResult(result, asJson);
            Environment.ExitCode = result.Success ? 0 : 1;
        }, nameOption, jsonOption);

        return cmd;
    }

    private static Command BuildLogsCommand(RunnerInstallService runnerService)
    {
        var nameOption = new Option<string?>("--name", "Runner name");
        var cmd = new Command("logs", "Follow local runner logs")
        {
            nameOption
        };

        cmd.SetHandler(async (string? name) =>
        {
            await runnerService.TailLogsAsync(name);
        }, nameOption);

        return cmd;
    }

    private static Command BuildStopCommand(RunnerInstallService runnerService)
    {
        var nameOption = new Option<string?>("--name", "Runner name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var cmd = new Command("stop", "Stop the local runner")
        {
            nameOption, jsonOption
        };

        cmd.SetHandler(async (string? name, bool asJson) =>
        {
            var result = await runnerService.StopAsync(name);
            WriteResult(result, asJson);
            Environment.ExitCode = result.Success ? 0 : 1;
        }, nameOption, jsonOption);

        return cmd;
    }

    private static Command BuildRestartCommand(RunnerInstallService runnerService)
    {
        var nameOption = new Option<string?>("--name", "Runner name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var cmd = new Command("restart", "Restart the local runner")
        {
            nameOption, jsonOption
        };

        cmd.SetHandler(async (string? name, bool asJson) =>
        {
            var result = await runnerService.RestartAsync(name);
            WriteResult(result, asJson);
            Environment.ExitCode = result.Success ? 0 : 1;
        }, nameOption, jsonOption);

        return cmd;
    }

    private static Command BuildUninstallCommand(RunnerInstallService runnerService)
    {
        var nameOption = new Option<string?>("--name", "Runner name");
        var deleteDataOption = new Option<bool>("--delete-data", "Also delete the local runner data / Docker volume");
        var yesOption = new Option<bool>("--yes", "Do not prompt for confirmation");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var cmd = new Command("uninstall", "Uninstall the local runner")
        {
            nameOption, deleteDataOption, yesOption, jsonOption
        };

        cmd.SetHandler(async (string? name, bool deleteData, bool yes, bool asJson) =>
        {
            if (!yes && !asJson)
            {
                var confirmed = PromptForYesNo(deleteData
                    ? "Uninstall the runner and delete its local data?"
                    : "Uninstall the runner and keep its local data?", false);
                if (!confirmed)
                {
                    var cancelled = new RunnerCommandResult
                    {
                        Success = false,
                        Summary = "Runner uninstall cancelled",
                        Checks = [new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, false, "cancelled by user")]
                    };
                    WriteResult(cancelled, asJson);
                    Environment.ExitCode = 1;
                    return;
                }
            }

            var result = await runnerService.UninstallAsync(name, deleteData);
            WriteResult(result, asJson);
            Environment.ExitCode = result.Success ? 0 : 1;
        }, nameOption, deleteDataOption, yesOption, jsonOption);

        return cmd;
    }

    private static bool PromptForYesNo(string label, bool defaultValue)
    {
        Console.Write($"{label} [{(defaultValue ? "Y/n" : "y/N")}]: ");
        var value = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public static void WriteResult(RunnerCommandResult result, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = result.Success,
                summary = result.Summary,
                checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = result.NextSteps,
                install = result.Install,
                status = result.Status,
                restartPolicy = result.RestartPolicy,
                containerImage = result.ContainerImage
            });
            return;
        }

        CliDiagnosticsWriter.Write(new CommandDiagnosticResult
        {
            Success = result.Success,
            Summary = result.Summary,
            Checks = result.Checks,
            NextSteps = result.NextSteps
        });

        if (result.Install != null)
        {
            var table = new Table();
            table.AddColumn("Field");
            table.AddColumn("Value");
            table.AddRow("runner", result.Install.RunnerName);
            table.AddRow("mode", result.Install.Mode);
            table.AddRow("server", result.Install.ApiServerUrl);
            if (!string.IsNullOrWhiteSpace(result.Status))
            {
                table.AddRow("status", result.Status);
            }
            if (!string.IsNullOrWhiteSpace(result.Install.ServiceName))
            {
                table.AddRow("service", result.Install.ServiceName);
            }
            if (!string.IsNullOrWhiteSpace(result.Install.DockerContainerName))
            {
                table.AddRow("container", result.Install.DockerContainerName);
            }
            if (!string.IsNullOrWhiteSpace(result.ContainerImage))
            {
                table.AddRow("image", result.ContainerImage);
            }
            if (!string.IsNullOrWhiteSpace(result.RestartPolicy))
            {
                table.AddRow("restart", result.RestartPolicy);
            }
            AnsiConsole.Write(table);
        }
    }
}
