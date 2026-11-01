using System.Text.Json;

namespace Automaton.Cli.Services;

/// <summary>
/// Asks the server which requested permissions still wait for a system administrator. Used by validate and
/// deploy, so an agent can hand the owner the approval link instead of debugging a refused API call.
/// </summary>
/// <summary>What to tell the user. <c>Waiting</c> is false when the status could not be checked.</summary>
public sealed record PermissionStatusOutcome(string Message, bool Waiting);

public static class PermissionStatusCheck
{
    public const string CheckName = "permissions";

    /// <summary>
    /// A warning naming waiting permissions with the approval link, a "could not check" note when the server
    /// did not answer, or null when nothing waits. Never throws for a failed check, so deploy still succeeds.
    /// </summary>
    public static async Task<PermissionStatusOutcome?> FindWaitingAsync(string server, string apiKey, string appId,
        IReadOnlyList<string>? requested, CancellationToken cancellationToken)
    {
        if (requested is not { Count: > 0 }) return null;
        JsonElement status;
        try
        {
            using var client = new AutomatonApiClient(server, apiKey);
            status = await client.GetPermissionsAsync(appId, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return new PermissionStatusOutcome($"could not check requested permissions: {exception.Message}", false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PermissionStatusOutcome("could not check requested permissions: the server did not answer in time", false);
        }
        catch (JsonException)
        {
            return new PermissionStatusOutcome("could not check requested permissions: the server sent an unexpected answer", false);
        }
        return Describe(status) is { } message ? new PermissionStatusOutcome(message, true) : null;
    }

    public static string? Describe(JsonElement status)
    {
        var waiting = status.TryGetProperty("waiting", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrEmpty(item)).ToList()
            : [];
        if (waiting.Count == 0) return null;
        var link = status.TryGetProperty("approvalUrl", out var url) && url.ValueKind == JsonValueKind.String
            ? $" A system administrator approves them at {url.GetString()}"
            : " A system administrator approves them in Studio: Admin → Apps";
        return $"permissions waiting for approval: {string.Join(", ", waiting)}; the app runs without that access until then.{link}";
    }
}
