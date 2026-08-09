using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

public static class TargetCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("target", "Manage deployment targets for the linked app");
        command.AddCommand(BuildList(config));
        command.AddCommand(BuildAdd(config));
        command.AddCommand(BuildRemove(config));
        command.AddCommand(BuildPrimary(config));
        command.AddCommand(BuildDefaultAlias(config));
        return command;
    }

    private static Command BuildList(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = EnvironmentOption();
        var command = new Command("list", "List deployment targets")
        {
            jsonOption,
            environmentOption
        };
        command.SetHandler(async (InvocationContext context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(jsonOption);
            var environmentName = context.ParseResult.GetValueForOption(environmentOption);
            try
            {
                var (appName, targets) = await new DeploymentTargetService(config)
                    .ListAsync(Directory.GetCurrentDirectory(), environmentName);
                if (asJson)
                {
                    CliJson.Write(new { success = true, appName, targets });
                }
                else
                {
                    WriteTargets(appName, targets);
                }
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteError(ex, asJson);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static Command BuildAdd(ConfigService config)
    {
        var slugArgument = new Argument<string>("slug", "Target slug");
        var runnerOption = new Option<string>("--runner", "Runner ID or exact Runner name")
        {
            IsRequired = true
        };
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = EnvironmentOption();
        var command = new Command("add", "Add a deployment target")
        {
            slugArgument,
            runnerOption,
            jsonOption,
            environmentOption
        };
        command.SetHandler(async (InvocationContext context) =>
        {
            var slug = context.ParseResult.GetValueForArgument(slugArgument);
            var runner = context.ParseResult.GetValueForOption(runnerOption);
            var asJson = context.ParseResult.GetValueForOption(jsonOption);
            var environmentName = context.ParseResult.GetValueForOption(environmentOption);
            try
            {
                var target = await new DeploymentTargetService(config).AddAsync(
                    Directory.GetCurrentDirectory(), slug, runner!, environmentName);
                if (asJson)
                {
                    CliJson.Write(new { success = true, target });
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[green]Target added:[/] {Markup.Escape(target.Slug)} → {Markup.Escape(target.Runner?.Name ?? "unassigned")}");
                }
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteError(ex, asJson);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static Command BuildRemove(ConfigService config)
    {
        var slugArgument = new Argument<string>("slug", "Target slug");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = EnvironmentOption();
        var command = new Command("remove", "Remove a deployment target")
        {
            slugArgument,
            jsonOption,
            environmentOption
        };
        command.SetHandler(async (InvocationContext context) =>
        {
            var slug = context.ParseResult.GetValueForArgument(slugArgument);
            var asJson = context.ParseResult.GetValueForOption(jsonOption);
            var environmentName = context.ParseResult.GetValueForOption(environmentOption);
            try
            {
                await new DeploymentTargetService(config).RemoveAsync(
                    Directory.GetCurrentDirectory(), slug, environmentName);
                if (asJson)
                {
                    CliJson.Write(new { success = true, removed = slug });
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]Target removed:[/] {Markup.Escape(slug)}");
                }
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteError(ex, asJson);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static Command BuildPrimary(ConfigService config)
    {
        var slugArgument = new Argument<string>("slug", "Target slug");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = EnvironmentOption();
        var command = new Command("primary", "Promote a deployment target to Live")
        {
            slugArgument,
            jsonOption,
            environmentOption
        };
        command.SetHandler(async (InvocationContext context) =>
        {
            var slug = context.ParseResult.GetValueForArgument(slugArgument);
            var asJson = context.ParseResult.GetValueForOption(jsonOption);
            var environmentName = context.ParseResult.GetValueForOption(environmentOption);
            try
            {
                await new DeploymentTargetService(config).DefaultAsync(
                    Directory.GetCurrentDirectory(), slug, environmentName);
                if (asJson)
                {
                    CliJson.Write(new { success = true, liveTarget = slug });
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]Live target:[/] {Markup.Escape(slug)}");
                }
                context.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteError(ex, asJson);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static Command BuildDefaultAlias(ConfigService config)
    {
        var command = BuildPrimary(config);
        command.Name = "default";
        command.Description = "Deprecated alias for target primary";
        command.IsHidden = true;
        return command;
    }

    private static Option<string?> EnvironmentOption() =>
        new("--env", "Use a named project environment");

    private static void WriteTargets(
        string appName,
        IReadOnlyList<DeploymentTargetInfo> targets)
    {
        AnsiConsole.MarkupLine($"[bold]Targets for {Markup.Escape(appName)}[/]");
        var table = new Table()
            .AddColumn("")
            .AddColumn("Target")
            .AddColumn("Runner")
            .AddColumn("Status");
        foreach (var target in targets)
        {
            table.AddRow(
                target.IsDefault ? "[green]*[/]" : "",
                Markup.Escape(target.Slug),
                Markup.Escape(target.Runner?.Name ?? "unassigned"),
                Markup.Escape(target.Runner is { IsOnline: false }
                    ? "Runner offline"
                    : target.Status));
        }
        AnsiConsole.Write(table);
    }

    private static void WriteError(Exception exception, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new { success = false, error = exception.Message });
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
        }
    }
}
