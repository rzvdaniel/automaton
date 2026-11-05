using System.Text.Json;

namespace Automaton.Cli.Services;

internal sealed record RunnerUpgradeOutcome(bool Success, string State, string Message);

/// <summary>Starts a Runner upgrade and, with wait, follows it until the Runner reconnected or was rolled back.</summary>
internal sealed class RunnerUpgradeRunner(AutomatonApiClient client, Func<TimeSpan, CancellationToken, Task> delay, Action<string> progress)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    internal async Task<RunnerUpgradeOutcome> RunAsync(string runnerId, bool wait, TimeSpan timeout, CancellationToken ct)
    {
        await client.StartRunnerUpgradeAsync(runnerId, ct);
        progress("Upgrade started. The Runner restarts on the new image.");
        if (!wait) return new(true, "InProgress", "Upgrade started. Check the result with: automaton runners");
        var deadline = DateTimeOffset.UtcNow + timeout;
        var last = "";
        while (DateTimeOffset.UtcNow < deadline)
        {
            await delay(PollInterval, ct);
            JsonElement status;
            try { status = await client.GetRunnerUpgradeAsync(runnerId, ct); }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                continue; // the API may be restarting
            }
            var state = status.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
            var message = status.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            if (state != last) progress(message);
            last = state;
            if (state == "Reconnected") return new(true, state, "The Runner reconnected on the current image.");
            if (state == "RolledBack") return new(false, state, message);
            if (state == "Unknown") return new(false, state, message);
        }
        return new(false, "InProgress", $"The Runner did not report back within {timeout.TotalSeconds:0} seconds. Check: automaton runners");
    }
}
