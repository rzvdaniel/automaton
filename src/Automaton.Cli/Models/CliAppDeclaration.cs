using System.Text.Json;

namespace Automaton.Cli.Models;

/// <summary>What the app declares in the latest deployed or saved automaton.json: kind, description, pages and OpenAPI path.</summary>
internal sealed record CliAppDeclaration(string Kind, string? Description, IReadOnlyList<CliAppPage> Pages, string? OpenApi)
{
    internal static CliAppDeclaration From(JsonElement app)
    {
        if (!app.TryGetProperty("application", out var application) || application.ValueKind != JsonValueKind.Object)
            return new CliAppDeclaration("web", null, [], null);
        var pages = new List<CliAppPage>();
        if (application.TryGetProperty("pages", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var page in list.EnumerateArray())
                pages.Add(new CliAppPage(Read(page, "path") ?? string.Empty, Read(page, "label") ?? string.Empty));
        var kind = string.Equals(Read(application, "kind"), "api", StringComparison.OrdinalIgnoreCase) ? "api" : "web";
        return new CliAppDeclaration(kind, Read(application, "description"), pages, Read(application, "openApi"));
    }

    private static string? Read(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal sealed record CliAppPage(string Path, string Label);
