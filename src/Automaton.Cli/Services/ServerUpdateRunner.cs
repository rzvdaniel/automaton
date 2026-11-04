using System.Text.RegularExpressions;
using Automaton.Cli.Models;

namespace Automaton.Cli.Services;

internal sealed record ServerUpdateOutcome(
    bool Success, string Message, string? RequestedVersion, ServerUpdateRun? Run, string? Log, bool Waited);

/// <summary>
/// Starts a server update and, with wait, follows it to the end. The API restarts during an update, so polling
/// tolerates it being unreachable. After success it verifies /health and the installed version.
/// </summary>
internal sealed partial class ServerUpdateRunner(
    AutomatonApiClient client, Func<TimeSpan, CancellationToken, Task> delay, Action<string> progress, TimeSpan? verifyFor = null)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VerifyFor = TimeSpan.FromMinutes(3);

    /// <summary>Accepts latest, server-vX.Y.Z or X.Y.Z; returns null when the text is not a version.</summary>
    internal static string? NormalizeVersion(string? text)
    {
        var version = string.IsNullOrWhiteSpace(text) ? "latest" : text.Trim();
        if (version == "latest" || ReleaseTag().IsMatch(version)) return version;
        return PlainVersion().IsMatch(version) ? "server-v" + version : null;
    }

    internal async Task<ServerUpdateOutcome> RunAsync(
        string version, bool wait, TimeSpan timeout, CancellationToken ct, bool allowDowngrade = false)
    {
        var before = await client.GetServerUpdateAsync(ct);
        if (before.Run is { State: "running" or "starting" } && !before.Stale)
            return Failure("An update is already running. Use automaton server update-status to follow it.", version, before);
        var previousStart = before.Run?.StartedAtUtc;

        await client.StartServerUpdateAsync(version, allowDowngrade, ct);
        progress($"Update to {version} requested.");
        if (!wait)
            return new(true, "Update requested. Follow it with: automaton server update-status", version, null, null, false);

        var finished = await WaitForRunAsync(previousStart, timeout, ct);
        if (finished is null)
            return new(false, $"The update did not finish within {timeout.TotalSeconds:0} seconds. Check: automaton server update-status", version, null, null, true);
        return await EvaluateAsync(finished, version, ct);
    }

    private async Task<ServerUpdateOutcome> EvaluateAsync(ServerUpdateStatus finished, string version, CancellationToken ct)
    {
        if (finished.RequestPending && finished.Hint is not null)
            return Failure("The update was not started. " + finished.Hint, version, finished);
        if (finished.Run is not { State: "succeeded" } run)
            return Failure(FailureMessage(finished), version, finished);

        var target = run.ToVersion ?? (version == "latest" ? null : version);
        if (version != "latest" && (NormalizeVersion(run.ToVersion) is not { } reported || reported != NormalizeVersion(version)))
            return Failure($"The update finished, but the server reports {run.ToVersion ?? "no version"} instead of {version}.", version, finished);
        var verification = await VerifyAsync(target, ct);
        return verification is null
            ? new(true, $"Updated to {run.ToVersion}. The API is healthy and reports the new version.", version, run, finished.Log, true)
            : Failure(verification, version, finished);
    }

    private async Task<ServerUpdateStatus?> WaitForRunAsync(DateTimeOffset? previousStart, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var wait = PollInterval;
        var lastState = "";
        while (DateTimeOffset.UtcNow < deadline)
        {
            await delay(wait, ct);
            try
            {
                var status = await client.GetServerUpdateAsync(ct);
                wait = PollInterval;
                if (status.RequestPending && status.Hint is not null) return status; // the host never picked the request up
                // A status from an earlier run is not ours: it started at the time we saw before requesting.
                if (status.Run is { } run && run.StartedAtUtc != previousStart)
                {
                    if (run.State != lastState) progress($"Update {run.State}.");
                    lastState = run.State;
                    if (run.State is "succeeded" or "failed" or "unknown") return status;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException
                                                  && !ct.IsCancellationRequested)
            {
                // The API restarts while the update replaces its container.
                progress("Waiting for the API to answer.");
                wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, MaxBackoff.TotalSeconds));
            }
        }
        return null;
    }

    private async Task<string?> VerifyAsync(string? expectedVersion, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + (verifyFor ?? VerifyFor);
        var problem = "The API did not answer after the update.";
        var wait = TimeSpan.FromSeconds(2);
        while (true)
        {
            try
            {
                var health = await client.GetHealthAsync(ct);
                var summary = await client.GetInstallationSummaryAsync(ct);
                if (health != "ok") problem = $"The API health check returned \"{health}\".";
                else if (expectedVersion is not null && (NormalizeVersion(summary.Version) is not { } reported || reported == "latest" || reported != NormalizeVersion(expectedVersion)))
                    problem = $"installation-status reports {summary.Version ?? "no version"} instead of {expectedVersion}.";
                else return null;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                problem = $"The API did not answer after the update: {exception.Message}";
            }
            if (DateTimeOffset.UtcNow >= deadline) return problem;
            await delay(wait, ct);
            wait = TimeSpan.FromSeconds(Math.Min(wait.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }
    }

    private static string FailureMessage(ServerUpdateStatus status)
    {
        var run = status.Run!;
        var backup = run.RecoveryBackup is null ? "" : $" Recovery backup: {run.RecoveryBackup}.";
        return $"The update failed{(run.ExitCode is { } code ? $" (exit code {code})" : "")}. {run.Message}{backup} " +
               "The previous release is kept; see docs/prod/install-automaton.md for recovery.";
    }

    private static ServerUpdateOutcome Failure(string message, string version, ServerUpdateStatus status)
        => new(false, message, version, status.Run, status.Log, true);

    [GeneratedRegex(@"^server-v[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex ReleaseTag();

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex PlainVersion();
}
