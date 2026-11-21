using Automaton.Cli.Services;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// <c>automaton docs APP</c> prints the README an app ships in <c>docs/README.md</c>; an app without docs prints its
/// description. <c>--open</c> opens the app's Docs tab in Studio instead.
/// </summary>
public static class DocsCommand
{
    internal const string EntryPage = "docs/README.md";

    public static Command Build(ConfigService config)
    {
        var app = new Argument<string>("app", "App ID or exact slug");
        var open = new Option<bool>("--open", "Open the app's Docs tab in Studio instead of printing the README");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("docs", "Print an app's docs (docs/README.md), or its description when it ships none") { app, open, json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Run automaton login to connect to your server.");
                using var client = new AutomatonApiClient(server, apiKey);
                var details = await client.GetAppByIdOrSlugAsync(context.ParseResult.GetValueForArgument(app), context.GetCancellationToken());
                var appId = details.GetProperty("id").GetString()!;
                if (context.ParseResult.GetValueForOption(open))
                {
                    var url = CliAppUrlBuilder.BuildStudioApplicationUrl(server, appId) + "?view=docs";
                    CliBrowserService.OpenUrl(url);
                    Console.WriteLine($"Opened: {url}");
                    return;
                }

                var files = await client.ListScopedFilesAsync(appId, "source");
                var shipsDocs = HasEntryPage(files);
                var text = shipsDocs
                    ? await client.ReadScopedFileAsync(appId, "source", EntryPage, context.GetCancellationToken())
                    : Description(details);
                if (asJson) CliJson.Write(new { success = true, appId, hasDocs = shipsDocs, content = text });
                else Console.WriteLine(text);
            }
            catch (Exception exception)
            {
                if (asJson) CliJson.Write(new { success = false, error = exception.Message });
                else Console.Error.WriteLine(exception.Message);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    internal static bool HasEntryPage(JsonElement files)
    {
        var list = files.ValueKind == JsonValueKind.Array ? files
            : files.ValueKind == JsonValueKind.Object && files.TryGetProperty("files", out var inner) ? inner
            : default;
        return list.ValueKind == JsonValueKind.Array && list.EnumerateArray().Any(file =>
            file.ValueKind == JsonValueKind.String
            && string.Equals(file.GetString()?.Replace('\\', '/'), EntryPage, StringComparison.Ordinal));
    }

    internal static string Description(JsonElement app)
    {
        var description = app.TryGetProperty("description", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(description) ? "This app ships no docs and has no description." : description;
    }
}
