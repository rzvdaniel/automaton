using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>Projects: create one, list yours, and add the linked app to one so it can use the project vault.</summary>
public static class ProjectCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("project", "Create projects, list yours and add the current app to one");
        command.AddCommand(BuildCreate(config));
        command.AddCommand(BuildList(config));
        command.AddCommand(BuildAddApp(config));
        command.AddCommand(BuildRemoveApp(config));
        command.AddCommand(ProjectRunnersCommand.Build(config));
        return command;
    }

    private static Option<bool> JsonOption() => new("--json", "Print the result as JSON");

    private static Command BuildCreate(ConfigService config)
    {
        var name = new Argument<string>("name", "Project name");
        var json = JsonOption();
        var command = new Command("create", "Create a project; you become its administrator") { name, json };
        command.SetHandler((string projectName, bool asJson) => RunAsync(asJson, async () =>
        {
            using var client = CreateClient(config);
            var project = await client.CreateProjectAsync(projectName);
            Report(asJson, new { success = true, project }, $"Created project {Markup.Escape(projectName)} ({Text(project, "id")}).");
        }), name, json);
        return command;
    }

    private static Command BuildList(ConfigService config)
    {
        var json = JsonOption();
        var command = new Command("list", "List the projects you belong to") { json };
        command.SetHandler((bool asJson) => RunAsync(asJson, async () =>
        {
            using var client = CreateClient(config);
            var projects = await client.ListProjectsAsync();
            if (asJson)
            {
                CliJson.Write(new { success = true, projects });
                return;
            }
            if (projects.GetArrayLength() == 0)
            {
                AnsiConsole.MarkupLine("You are not in any project. Create one with [bold]automaton project create NAME[/].");
                return;
            }
            var table = new Table().AddColumns("Project", "Id", "Role");
            foreach (var item in projects.EnumerateArray())
                table.AddRow(Markup.Escape(Text(item, "displayName")), Text(item, "id"), Text(item, "role"));
            AnsiConsole.Write(table);
        }), json);
        return command;
    }

    private static Command BuildAddApp(ConfigService config)
    {
        var project = new Argument<string?>("project", () => null, "Project name or id (optional when you belong to exactly one project)");
        var json = JsonOption();
        var command = new Command("add-app", "Add the current app to a project") { project, json };
        command.SetHandler((string? projectRef, bool asJson) => RunAsync(asJson, async () =>
        {
            var (server, apiKey, appId) = ResolveLinkedApp(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var projects = await client.ListProjectsAsync();
            var (id, display) = PickProject(projects, projectRef);
            await client.AddAppToProjectAsync(id, appId);
            Report(asJson, new { success = true, projectId = id, appId }, $"Added this app to project {Markup.Escape(display)}.");
        }), project, json);
        return command;
    }

    private static Command BuildRemoveApp(ConfigService config)
    {
        var project = new Argument<string?>("project", () => null, "Project name or id; defaults to the linked app's project");
        var json = JsonOption();
        var command = new Command("remove-app", "Remove the current app from its project so its owner can delete it") { project, json };
        command.SetHandler((string? projectRef, bool asJson) => RunAsync(asJson, async () =>
        {
            var (server, apiKey, appId) = ResolveLinkedApp(config);
            using var client = new AutomatonApiClient(server, apiKey);
            var app = await client.GetAppAsync(appId);
            var linkedProjectId = Text(app, "projectId");
            if (string.IsNullOrWhiteSpace(linkedProjectId))
                throw new InvalidOperationException("This app is not in a project.");
            var (id, display) = PickProject(await client.ListProjectsAsync(), projectRef ?? linkedProjectId);
            if (id != linkedProjectId)
                throw new InvalidOperationException("This app is linked to a different project.");
            await client.RemoveAppFromProjectAsync(id, appId);
            Report(asJson, new { success = true, projectId = id, appId }, $"Removed this app from project {Markup.Escape(display)}.");
        }), project, json);
        return command;
    }

    /// <summary>Finds the project by id or name; with no reference it must be the only one.</summary>
    internal static (string Id, string DisplayName) PickProject(JsonElement projects, string? reference)
    {
        var all = projects.ValueKind == JsonValueKind.Array ? projects.EnumerateArray().ToList() : [];
        var matches = string.IsNullOrWhiteSpace(reference)
            ? all
            : all.Where(item => string.Equals(Text(item, "id"), reference, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Text(item, "name"), reference, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Text(item, "displayName"), reference, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 1)
            return (Text(matches[0], "id"), Text(matches[0], "displayName"));
        if (matches.Count == 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(reference)
                ? "You are not in any project. Create one with automaton project create NAME."
                : $"No project named or with id '{reference}'. Run automaton project list.");
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(reference)
            ? "You belong to several projects; name one: automaton project add-app PROJECT."
            : $"More than one project matches '{reference}'; use its id from automaton project list.");
    }

    private static AutomatonApiClient CreateClient(ConfigService config)
    {
        var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
        return new AutomatonApiClient(server, apiKey);
    }

    private static (string Server, string ApiKey, string AppId) ResolveLinkedApp(ConfigService config)
    {
        var directory = Directory.GetCurrentDirectory();
        var project = config.TryLoadProjectConfig(directory)
            ?? throw new InvalidOperationException("automaton.json was not found.");
        var resolved = config.ResolveContext(project, directory, null);
        if (string.IsNullOrWhiteSpace(resolved.AppId))
            throw new InvalidOperationException("The project is not linked to an Automaton app.");
        if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
            throw new InvalidOperationException("Automaton server credentials are not configured.");
        return (resolved.Server, resolved.ApiKey, resolved.AppId);
    }

    private static async Task RunAsync(bool asJson, Func<Task> action)
    {
        try
        {
            await action();
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
            Environment.ExitCode = 1;
        }
    }

    private static void Report(bool asJson, object json, string text)
    {
        if (asJson) CliJson.Write(json);
        else AnsiConsole.MarkupLine(text);
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
