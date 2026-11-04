using System.Text.Json;

namespace Automaton.Cli.Commands;

internal static class RunnerReferenceResolver
{
    internal static string Pick(JsonElement runners, string reference, string idProperty, string nameProperty)
    {
        var matches = runners.ValueKind == JsonValueKind.Array
            ? runners.EnumerateArray().Where(item =>
                string.Equals(Text(item, idProperty), reference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Text(item, nameProperty), reference, StringComparison.OrdinalIgnoreCase)).ToList()
            : [];
        return matches.Count switch
        {
            1 => Text(matches[0], idProperty),
            0 => throw new InvalidOperationException($"Runner '{reference}' was not found in this scope."),
            _ => throw new InvalidOperationException($"More than one Runner matches '{reference}'; use its ID.")
        };
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
