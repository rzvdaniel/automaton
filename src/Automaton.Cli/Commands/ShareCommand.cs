using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>
/// Who can open the linked app: only you, people you choose, everyone on this server, or anyone on the internet.
/// The same choices as Studio's Share dialog, so an AI agent can act for the user.
/// </summary>
public static class ShareCommand
{
    private static readonly string[] Audiences = ["me", "people", "team", "server", "internet"];

    public static Command Build(ConfigService config)
    {
        var command = new Command("share", "Show or change who can open this app (same choices as Studio's Share dialog)");
        command.AddCommand(BuildStatus(config));
        command.AddCommand(BuildSet(config));
        command.AddCommand(BuildAdd(config));
        command.AddCommand(BuildRemove(config));
        return command;
    }

    private static Option<bool> JsonOption() => new("--json", "Print the result as JSON");

    private static Command BuildStatus(ConfigService config)
    {
        var json = JsonOption();
        var command = new Command("status", "Show who can open the app and the people it is shared with") { json };
        command.SetHandler((bool asJson) => RunAsync(asJson, async () =>
        {
            var (client, appId) = Connect(config);
            using (client) Write(await client.GetSharingAsync(appId), asJson);
        }), json);
        return command;
    }

    private static Command BuildSet(ConfigService config)
    {
        var audience = new Argument<string>("audience", "me (only you), people (the people you added), team (members of --team), server (everyone on this server) or internet (anyone, with a public address)");
        var slug = new Option<string?>(["--slug", "-s"], "Public address name for 'internet' (kept from before when omitted)");
        var team = new Option<string?>("--team", "Project (team) name or id for 'team'; its members open the app after signing in. See: automaton project list");
        var list = new Option<bool>("--list", "With 'internet': list the app in the store (text and images: automaton listing)");
        var noList = new Option<bool>("--no-list", "With 'internet': keep the app out of the store");
        var json = JsonOption();
        var command = new Command("set", "Choose who can open the app") { audience, slug, team, list, noList, json };
        command.SetHandler((InvocationContext context) =>
        {
            var parse = context.ParseResult;
            var asJson = parse.GetValueForOption(json);
            return RunAsync(asJson, async () =>
            {
                var choice = parse.GetValueForArgument(audience);
                if (!Audiences.Contains(choice, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Choose one of: {string.Join(", ", Audiences)}.");
                if (parse.GetValueForOption(list) && parse.GetValueForOption(noList))
                    throw new InvalidOperationException("Use either --list or --no-list, not both.");
                if ((parse.GetValueForOption(list) || parse.GetValueForOption(noList)) && choice != "internet")
                    throw new InvalidOperationException("--list and --no-list only apply to 'internet'.");
                bool? listed = parse.GetValueForOption(list) ? true : parse.GetValueForOption(noList) ? false : null;
                var (client, appId) = Connect(config);
                using (client)
                {
                    string? teamId = null;
                    if (choice == "team")
                    {
                        var reference = parse.GetValueForOption(team) ?? throw new InvalidOperationException("Name the team: --team NAME.");
                        teamId = ProjectCommand.PickProject(await client.ListProjectsAsync(), reference).Id;
                    }
                    Write(await client.SetSharingAsync(appId, choice, parse.GetValueForOption(slug), teamId, listed), asJson);
                }
            });
        });
        return command;
    }

    private static Command BuildAdd(ConfigService config)
    {
        var email = new Argument<string>("email", "Email address of a person on this server");
        var json = JsonOption();
        var command = new Command("add", "Let a person on this server open the app") { email, json };
        command.SetHandler((string address, bool asJson) => RunAsync(asJson, async () =>
        {
            var (client, appId) = Connect(config);
            using (client) Write(await client.AddSharedPersonAsync(appId, address), asJson);
        }), email, json);
        return command;
    }

    private static Command BuildRemove(ConfigService config)
    {
        var email = new Argument<string>("email", "Email address of a person the app is shared with");
        var json = JsonOption();
        var command = new Command("remove", "Stop sharing the app with a person") { email, json };
        command.SetHandler((string address, bool asJson) => RunAsync(asJson, async () =>
        {
            var (client, appId) = Connect(config);
            using (client)
            {
                var current = await client.GetSharingAsync(appId);
                var person = current.GetProperty("people").EnumerateArray().FirstOrDefault(item =>
                    string.Equals(item.GetProperty("email").GetString(), address.Trim(), StringComparison.OrdinalIgnoreCase));
                if (person.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"The app is not shared with {address}.");
                Write(await client.RemoveSharedPersonAsync(appId, person.GetProperty("userId").GetString()!), asJson);
            }
        }), email, json);
        return command;
    }

    private static void Write(JsonElement state, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new { success = true, state });
            return;
        }
        var audience = state.GetProperty("audience").GetString();
        AnsiConsole.MarkupLine(audience switch
        {
            "internet" => "[bold]Anyone on the internet[/] can open this app.",
            "server" => "[bold]Everyone on this server[/] can open this app.",
            "team" => $"[bold]Members of {Markup.Escape(state.GetProperty("team").GetProperty("name").GetString() ?? "the team")}[/] can open this app.",
            "people" => "[bold]People you choose[/] can open this app.",
            _ => "[bold]Only you[/] can open this app."
        });
        if (audience == "internet")
            AnsiConsole.MarkupLine(state.TryGetProperty("listed", out var listed) && listed.ValueKind == JsonValueKind.True
                ? "Listed in the store."
                : "Not listed in the store (--list to list it).");
        if (state.TryGetProperty("publicUrlSlug", out var slug) && slug.ValueKind == JsonValueKind.String)
            AnsiConsole.MarkupLine($"Public address name: {Markup.Escape(slug.GetString()!)}");
        foreach (var person in state.GetProperty("people").EnumerateArray())
            AnsiConsole.MarkupLine($"  shared with {Markup.Escape(person.GetProperty("email").GetString() ?? string.Empty)}");
    }

    private static (AutomatonApiClient Client, string AppId) Connect(ConfigService config)
    {
        var directory = Directory.GetCurrentDirectory();
        var project = config.TryLoadProjectConfig(directory)
            ?? throw new InvalidOperationException("automaton.json was not found.");
        var resolved = config.ResolveContext(project, directory, null);
        if (string.IsNullOrWhiteSpace(resolved.AppId))
            throw new InvalidOperationException("The project is not linked to an Automaton app.");
        if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
            throw new InvalidOperationException("Automaton server credentials are not configured.");
        return (new AutomatonApiClient(resolved.Server, resolved.ApiKey), resolved.AppId);
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
}
