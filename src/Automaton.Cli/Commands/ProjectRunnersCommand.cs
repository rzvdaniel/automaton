using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>Lists and changes the Runners shared with a project.</summary>
public static class ProjectRunnersCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("runners", "List, add, or remove project Runners");
        command.AddCommand(BuildList(config));
        command.AddCommand(BuildChange(config, add: true));
        command.AddCommand(BuildChange(config, add: false));
        return command;
    }

    private static Command BuildList(ConfigService config)
    {
        var project = new Option<string?>("--project", "Project ID or name; defaults to the linked app's project");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("list", "List Runners linked to a project") { project, json };
        command.SetHandler((InvocationContext context) => RunAsync(context, context.ParseResult.GetValueForOption(json), async () =>
        {
            using var client = CreateClient(config);
            var projectRef = context.ParseResult.GetValueForOption(project);
            var asJson = context.ParseResult.GetValueForOption(json);
            var (id, name) = await ResolveProjectAsync(config, client, projectRef);
            var runners = await client.ListProjectRunnersAsync(id);
            if (asJson) CliJson.Write(new { success = true, projectId = id, runners });
            else
            {
                var table = new Table().AddColumns("Runner", "ID");
                foreach (var runner in runners.EnumerateArray())
                    table.AddRow(Markup.Escape(Text(runner, "runnerName")), Text(runner, "runnerId"));
                AnsiConsole.Write(table);
            }
        }));
        return command;
    }

    private static Command BuildChange(ConfigService config, bool add)
    {
        var runner = new Argument<string>("runner", "Runner ID or exact name");
        var project = new Option<string?>("--project", "Project ID or name; defaults to the linked app's project");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var verb = add ? "add" : "remove";
        var command = new Command(verb, $"{(add ? "Add a personal Runner to" : "Remove a Runner from")} a project") { runner, project, json };
        command.SetHandler((InvocationContext context) => RunAsync(context, context.ParseResult.GetValueForOption(json), async () =>
        {
            using var client = CreateClient(config);
            var runnerRef = context.ParseResult.GetValueForArgument(runner);
            var projectRef = context.ParseResult.GetValueForOption(project);
            var asJson = context.ParseResult.GetValueForOption(json);
            var (projectId, _) = await ResolveProjectAsync(config, client, projectRef);
            var available = add ? await client.ListRunnersAsync() : await client.ListProjectRunnersAsync(projectId);
            var runnerId = RunnerReferenceResolver.Pick(available, runnerRef, add ? "id" : "runnerId", add ? "name" : "runnerName");
            if (add) await client.AddRunnerToProjectAsync(projectId, runnerId);
            else await client.RemoveRunnerFromProjectAsync(projectId, runnerId);
            if (asJson) CliJson.Write(new { success = true, projectId, runnerId, action = verb });
            else AnsiConsole.MarkupLine($"[green]Runner {Markup.Escape(runnerId)} {(add ? "added to" : "removed from")} project.[/]");
        }));
        return command;
    }

    private static async Task<(string Id, string Name)> ResolveProjectAsync(ConfigService config, AutomatonApiClient client, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            var directory = Directory.GetCurrentDirectory();
            var local = config.TryLoadProjectConfig(directory);
            if (local is not null && config.ResolveContext(local, directory).AppId is { Length: > 0 } appId)
            {
                var app = await client.GetAppAsync(appId);
                reference = Text(app, "projectId");
            }
        }
        return ProjectCommand.PickProject(await client.ListProjectsAsync(), reference);
    }

    private static AutomatonApiClient CreateClient(ConfigService config)
    {
        var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
        return new AutomatonApiClient(server, apiKey);
    }

    private static async Task RunAsync(InvocationContext context, bool asJson, Func<Task> action)
    {
        try { await action(); context.ExitCode = 0; }
        catch (Exception exception)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            context.ExitCode = 1;
        }
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
