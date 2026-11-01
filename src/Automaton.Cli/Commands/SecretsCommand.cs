using System.CommandLine;
using System.Text.Json;
using Automaton.Cli.Services;
using Automaton.Core.Models;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>
/// Secrets for the linked app: its own, its project's vault and the caller's vault. Values are read from
/// standard input, never from arguments, so they stay out of shell history and AI chat transcripts.
/// </summary>
public static class SecretsCommand
{
    public static Command Build(ConfigService config)
    {
        var command = new Command("secrets",
            "Check and fill the secrets this app declares in automaton.json (application.secrets)");
        command.AddCommand(BuildStatus(config));
        command.AddCommand(BuildList(config));
        command.AddCommand(BuildSet(config));
        command.AddCommand(BuildDelete(config));
        return command;
    }

    private static Option<bool> JsonOption() => new("--json", "Print the result as JSON");
    private static Option<bool> ProjectOption() => new("--project", "Use the app's project vault, shared by the project's apps");
    private static Option<bool> UserOption() => new("--user", "Use your personal vault, shared by your personal apps");

    private static Command BuildStatus(ConfigService config)
    {
        var json = JsonOption();
        var command = new Command("status", "Show each declared secret: present or missing, and which scope provides it") { json };
        command.SetHandler((bool asJson) => RunAsync(asJson, async () =>
        {
            var (client, appId, declarations) = Connect(config);
            using (client)
            {
                if (declarations.Count == 0)
                {
                    Report(asJson, new { success = true, secrets = Array.Empty<object>() },
                        "automaton.json declares no secrets (application.secrets).");
                    return;
                }
                var status = await client.GetSecretStatusAsync(appId, declarations);
                var missing = status.EnumerateArray().Count(item => !item.GetProperty("present").GetBoolean());
                if (asJson)
                {
                    CliJson.Write(new { success = true, missing, secrets = status });
                    return;
                }
                var table = new Table().AddColumns("Secret", "Status", "Provided by", "Description");
                foreach (var item in status.EnumerateArray())
                {
                    var present = item.GetProperty("present").GetBoolean();
                    table.AddRow(
                        Markup.Escape(item.GetProperty("name").GetString() ?? ""),
                        present ? "[green]present[/]" : $"[yellow]missing[/] {Markup.Escape(Text(item, "problem"))}",
                        Markup.Escape(Text(item, "providedBy")),
                        Markup.Escape(Text(item, "description")));
                }
                AnsiConsole.Write(table);
                if (missing > 0)
                    AnsiConsole.MarkupLine("Fill a missing secret with [bold]automaton secrets set NAME[/] [grey](--project or --user for a shared vault)[/], passing the value on standard input.");
            }
        }), json);
        return command;
    }

    private static Command BuildList(ConfigService config)
    {
        var json = JsonOption();
        var project = ProjectOption();
        var user = UserOption();
        var command = new Command("list", "List secret names in the app, its project vault or your vault (values are never shown)") { project, user, json };
        command.SetHandler((bool inProject, bool inUser, bool asJson) => RunAsync(asJson, async () =>
        {
            var scope = Scope(inProject, inUser);
            var (client, appId, _) = Connect(config);
            using (client)
            {
                var secrets = await client.ListSecretsAsync(appId, scope);
                if (asJson)
                {
                    CliJson.Write(new { success = true, scope, secrets });
                    return;
                }
                if (secrets.GetArrayLength() == 0)
                {
                    AnsiConsole.MarkupLine($"No secrets in the {scope} scope.");
                    return;
                }
                var table = new Table().AddColumns("Secret", "Version", "Updated (UTC)", "Description");
                foreach (var item in secrets.EnumerateArray())
                    table.AddRow(Markup.Escape(Text(item, "name")), Text(item, "version"),
                        Markup.Escape(Text(item, "updatedAtUtc")), Markup.Escape(Text(item, "description")));
                AnsiConsole.Write(table);
            }
        }), project, user, json);
        return command;
    }

    private static Command BuildSet(ConfigService config)
    {
        var name = new Argument<string>("name", "Environment variable name, for example STRIPE_KEY");
        var description = new Option<string?>("--description", "One-line explanation of what the secret is for");
        var json = JsonOption();
        var project = ProjectOption();
        var user = UserOption();
        var command = new Command("set",
            "Set a secret from standard input, for example: printf '%s' \"$VALUE\" | automaton secrets set NAME")
        { name, project, user, description, json };
        command.SetHandler((string secretName, bool inProject, bool inUser, string? text, bool asJson) => RunAsync(asJson, async () =>
        {
            var scope = Scope(inProject, inUser);
            var value = ReadValue(secretName, asJson);
            var (client, appId, _) = Connect(config);
            using (client)
            {
                var saved = await client.SetSecretAsync(appId, scope, secretName, value, text);
                Report(asJson, new { success = true, scope, secret = saved },
                    $"Saved {secretName} in the {scope} scope (version {Text(saved, "version")}). Running apps use it after their next start; redeploy to apply now.");
            }
        }), name, project, user, description, json);
        return command;
    }

    private static Command BuildDelete(ConfigService config)
    {
        var name = new Argument<string>("name", "Secret name");
        var json = JsonOption();
        var project = ProjectOption();
        var user = UserOption();
        var command = new Command("delete", "Delete a secret from the app, its project vault or your vault") { name, project, user, json };
        command.SetHandler((string secretName, bool inProject, bool inUser, bool asJson) => RunAsync(asJson, async () =>
        {
            var scope = Scope(inProject, inUser);
            var (client, appId, _) = Connect(config);
            using (client)
            {
                var deleted = await client.DeleteSecretAsync(appId, scope, secretName);
                if (!deleted) throw new InvalidOperationException($"{secretName} does not exist in the {scope} scope.");
                Report(asJson, new { success = true, scope, deleted = secretName }, $"Deleted {secretName} from the {scope} scope.");
            }
        }), name, project, user, json);
        return command;
    }

    private static string Scope(bool project, bool user)
    {
        if (project && user) throw new InvalidOperationException("Choose either --project or --user, not both.");
        return project ? "project" : user ? "user" : "app";
    }

    /// <summary>Reads the value from piped standard input, or prompts without echo in a terminal.</summary>
    internal static string ReadValue(string name, bool asJson, TextReader? input = null, bool? redirected = null)
    {
        string value;
        if (redirected ?? Console.IsInputRedirected)
        {
            value = (input ?? Console.In).ReadToEnd();
            // Drop one trailing line ending added by echo or a here-string, nothing else.
            if (value.EndsWith("\r\n", StringComparison.Ordinal)) value = value[..^2];
            else if (value.EndsWith('\n')) value = value[..^1];
        }
        else if (asJson)
            throw new InvalidOperationException("Pass the secret value on standard input.");
        else
            value = AnsiConsole.Prompt(new TextPrompt<string>($"Value for {Markup.Escape(name)}:").Secret());
        if (value.Length == 0) throw new InvalidOperationException("The secret value is empty.");
        return value;
    }

    private static (AutomatonApiClient Client, string AppId, IReadOnlyList<ApplicationSecretDeclaration> Declarations) Connect(ConfigService config)
    {
        var directory = Directory.GetCurrentDirectory();
        var project = config.TryLoadProjectConfig(directory)
            ?? throw new InvalidOperationException("automaton.json was not found.");
        var resolved = config.ResolveContext(project, directory, null);
        if (string.IsNullOrWhiteSpace(resolved.AppId))
            throw new InvalidOperationException("The project is not linked to an Automaton app.");
        if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
            throw new InvalidOperationException("Automaton server credentials are not configured.");
        return (new AutomatonApiClient(resolved.Server, resolved.ApiKey), resolved.AppId, project.Application.Secrets ?? []);
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
        else AnsiConsole.MarkupLine(Markup.Escape(text));
    }

    private static string Text(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : string.Empty;
}
