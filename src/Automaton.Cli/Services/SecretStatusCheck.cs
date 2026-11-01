using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Cli.Services;

/// <summary>Asks the server which declared secrets have no value yet. Used by validate and deploy.</summary>
public static class SecretStatusCheck
{
    public const string CheckName = "secrets";

    /// <summary>A warning message naming missing secrets, or null when all are present or none are declared.</summary>
    public static async Task<string?> FindMissingAsync(string server, string apiKey, string appId,
        IReadOnlyList<ApplicationSecretDeclaration>? declarations, CancellationToken cancellationToken)
    {
        if (declarations is not { Count: > 0 }) return null;
        JsonElement status;
        try
        {
            using var client = new AutomatonApiClient(server, apiKey);
            status = await client.GetSecretStatusAsync(appId, declarations, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return $"could not check declared secrets: {exception.Message}";
        }
        return Describe(status);
    }

    public static string? Describe(JsonElement status)
    {
        var missing = status.EnumerateArray()
            .Where(item => !item.GetProperty("present").GetBoolean())
            .Select(item => item.GetProperty("name").GetString())
            .ToList();
        return missing.Count == 0
            ? null
            : $"declared secrets without a value: {string.Join(", ", missing)}. Run 'automaton secrets status', then 'automaton secrets set NAME' with the value on standard input";
    }
}
