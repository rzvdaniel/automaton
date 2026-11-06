using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>automaton catalog sources: list, add, enable, disable and remove the catalog sources of the server (system administrator).</summary>
public static class CatalogSourcesCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("sources", "Manage the catalog sources of the server (system administrator): list, add, enable, disable, remove");
        command.AddCommand(BuildList(config));
        command.AddCommand(BuildAdd(config));
        command.AddCommand(BuildEnabled(config, "enable", true));
        command.AddCommand(BuildEnabled(config, "disable", false));
        command.AddCommand(BuildRemove(config));
        return command;
    }

    private static Option<bool> JsonOption() => new("--json", "Print the result as JSON (for agents)");

    private static Command BuildList(ConfigService config)
    {
        var json = JsonOption();
        var command = new Command("list", "List the catalog sources and whether each is used.\nExample: automaton catalog sources list") { json };
        command.SetHandler((bool asJson) => RunAsync(asJson, async () =>
        {
            using var client = Connect(config);
            var sources = await client.ListCatalogSourcesAsync();
            if (asJson) { CliJson.Write(new { success = true, sources }); return; }
            var table = new Table().AddColumns("Id", "Name", "Repository", "Branch", "State", "Private");
            foreach (var source in sources.EnumerateArray())
                table.AddRow(Text(source, "id"), Markup.Escape(Text(source, "name")), Markup.Escape(Text(source, "repo")), Markup.Escape(Text(source, "branch")),
                    source.GetProperty("enabled").GetBoolean() ? "[green]enabled[/]" : "[grey]disabled[/]", source.GetProperty("hasToken").GetBoolean() ? "yes" : "");
            AnsiConsole.Write(table);
        }), json);
        return command;
    }

    private static Command BuildAdd(ConfigService config)
    {
        var repo = new Argument<string>("repository", "owner/name or https://github.com/owner/name");
        var branch = new Option<string?>("--branch", "Branch that holds catalog.json (default: main)");
        var name = new Option<string?>("--name", "How the source is shown in the Catalog (default: the repository name)");
        var privateRepo = new Option<bool>("--private", "The repository is private: read its token from --token-file, --token-stdin or a hidden prompt");
        var tokenFile = new Option<string?>("--token-file", "File that holds the read token of a private repository (never pass a token as an argument)");
        var tokenStdin = new Option<bool>("--token-stdin", "Read the token from standard input");
        var dryRun = new Option<bool>("--dry-run", "Check the repository, branch, catalog.json and token and print what would be added; add nothing");
        var json = JsonOption();
        var command = new Command("add",
            "Add a catalog source.\nPublic:  automaton catalog sources add https://github.com/acme/apps\n" +
            "Private: automaton catalog sources add https://github.com/acme/apps --private --token-file ~/catalog-token\n" +
            "Check first: add --dry-run. A token is a fine-grained, read-only Contents token for that one repository.")
        { repo, branch, name, privateRepo, tokenFile, tokenStdin, dryRun, json };
        command.SetHandler(async context =>
        {
            var parsed = context.ParseResult;
            var address = parsed.GetValueForArgument(repo);
            var branchName = parsed.GetValueForOption(branch);
            var label = parsed.GetValueForOption(name);
            var dry = parsed.GetValueForOption(dryRun);
            var isPrivate = parsed.GetValueForOption(privateRepo);
            var file = parsed.GetValueForOption(tokenFile);
            var asJson = parsed.GetValueForOption(json);
            await RunAsync(asJson, async () =>
            {
                var token = CatalogSourceToken.Read(file, parsed.GetValueForOption(tokenStdin), isPrivate, AddHint(address, branchName, label), inputRedirected: asJson ? true : null);
                using var client = Connect(config);
                JsonElement result;
                try { result = await client.AddCatalogSourceAsync(address, branchName, label, token, dry); }
                catch (InvalidOperationException failure) when (token is null && !isPrivate && LooksPrivate(failure.Message))
                {
                    throw new InvalidOperationException($"{failure.Message}\nIf the repository is private: {CatalogSourceToken.PrivateNeedsPerson(AddHint(address, branchName, label))}");
                }
                var apps = result.TryGetProperty("appCount", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : (int?)null;
                if (asJson) CliJson.Write(new { success = true, dryRun = dry, source = result });
                else Console.WriteLine(dry
                    ? $"Dry run: {Text(result, "name")} ({Text(result, "repo")}, branch {Text(result, "branch")}) can be read and lists {apps} app(s). Nothing was added."
                    : $"Added {Text(result, "name")} ({Text(result, "id")}): {apps} app(s) found in its catalog.");
            });
        });
        return command;
    }

    private static Command BuildEnabled(ConfigService config, string verb, bool enabled)
    {
        var id = new Argument<string>("id", "The source id from automaton catalog sources list");
        var json = JsonOption();
        var command = new Command(verb, $"{(enabled ? "Use" : "Stop using")} a catalog source.\nExample: automaton catalog sources {verb} <id>") { id, json };
        command.SetHandler((string sourceId, bool asJson) => RunAsync(asJson, async () =>
        {
            using var client = Connect(config);
            var source = await client.SetCatalogSourceEnabledAsync(sourceId, enabled);
            if (asJson) CliJson.Write(new { success = true, source });
            else Console.WriteLine($"{Text(source, "name")} is now {(enabled ? "enabled" : "disabled")}.");
        }), id, json);
        return command;
    }

    private static Command BuildRemove(ConfigService config)
    {
        var id = new Argument<string>("id", "The source id from automaton catalog sources list");
        var json = JsonOption();
        var command = new Command("remove", "Remove a catalog source and its saved catalog; apps already installed keep running.\nExample: automaton catalog sources remove <id>") { id, json };
        command.SetHandler((string sourceId, bool asJson) => RunAsync(asJson, async () =>
        {
            using var client = Connect(config);
            await client.RemoveCatalogSourceAsync(sourceId);
            if (asJson) CliJson.Write(new { success = true, removed = sourceId });
            else Console.WriteLine($"Removed catalog source {sourceId}.");
        }), id, json);
        return command;
    }

    /// <summary>The exact command a person runs for their private repository; it names no secret.</summary>
    internal static string AddHint(string address, string? branch, string? name) =>
        $"automaton catalog sources add {Quote(address)}{(branch is null ? "" : $" --branch {Quote(branch)}")}{(name is null ? "" : $" --name {Quote(name)}")} --private --token-file ~/catalog-token";

    private static string Quote(string value) => value.All(c => char.IsLetterOrDigit(c) || "/:._-".Contains(c)) ? value : "'" + value.Replace("'", "'\\''") + "'";

    private static bool LooksPrivate(string message) => message.Contains("GitHub answered 404") || message.Contains("GitHub answered 401") || message.Contains("GitHub answered 403");

    private static AutomatonApiClient Connect(ConfigService config)
    {
        var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
        return new AutomatonApiClient(server, apiKey);
    }

    private static async Task RunAsync(bool asJson, Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or InvalidDataException)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else Console.Error.WriteLine($"Error: {exception.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : string.Empty;
}
