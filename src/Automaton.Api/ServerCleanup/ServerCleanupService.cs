using System.Globalization;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.ServerCleanup;

public sealed record ServerCleanupSettingsDto(string TimeUtc, int KeepDays, int JournalLimitMb);

public sealed record ServerCleanupRunDto(Guid Id, string RunnerName, DateTime StartedAtUtc, DateTime? FinishedAtUtc,
    string Result, long BytesFreed, string Trigger, string? Message);

public sealed record ServerCleanupResponse(ServerCleanupSettingsDto Settings, DateTime NextRunAtUtc, IReadOnlyList<ServerCleanupRunDto> Runs);

/// <summary>
/// The nightly host cleanup: settings, schedule and run history live here; the Runner on each server host does the work
/// (unused Docker images and build cache older than N days, archived journal above a limit).
/// </summary>
public sealed class ServerCleanupService(ApplicationDbContext db, IRunnerActionCommands runner)
{
    public const string Queued = "queued";
    private static readonly TimeSpan MissedSlotGrace = TimeSpan.FromHours(12);
    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan History = TimeSpan.FromDays(30);

    public async Task<ServerCleanupResponse> GetAsync(DateTime now, CancellationToken token)
    {
        var settings = await SettingsAsync(token);
        var runs = (await db.ServerCleanupRuns.AsNoTracking().OrderByDescending(run => run.StartedAtUtc).Take(20).ToListAsync(token))
            .Select(run => new ServerCleanupRunDto(run.Id, run.RunnerName, run.StartedAtUtc, run.FinishedAtUtc, run.Result,
                run.BytesFreed, run.Trigger, run.Message)).ToList();
        return new(ToDto(settings), NextSlot(settings, now), runs);
    }

    public async Task<ServerCleanupResponse> UpdateAsync(ServerCleanupSettingsDto request, DateTime now, CancellationToken token)
    {
        if (!TimeOnly.TryParseExact(request.TimeUtc, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("The time must look like 05:00 (UTC).");
        if (request.KeepDays is < 1 or > 365) throw new ArgumentException("Keep days must be from 1 to 365.");
        if (request.JournalLimitMb is < 50 or > 100_000) throw new ArgumentException("The journal limit must be from 50 to 100000 MB.");
        var settings = await SettingsAsync(token);
        settings.MinutesAfterMidnightUtc = time.Hour * 60 + time.Minute;
        settings.KeepDays = request.KeepDays;
        settings.JournalLimitMb = request.JournalLimitMb;
        settings.UpdatedAtUtc = now;
        await db.SaveChangesAsync(token);
        return await GetAsync(now, token);
    }

    /// <summary>Queues a run on every online server host. Throws when none is online.</summary>
    public async Task<int> RunNowAsync(DateTime now, CancellationToken token)
    {
        var queued = await QueueAsync(ServerCleanupTriggers.Manual, now, token);
        if (queued == 0) throw new InvalidOperationException("No server Runner is online, so there is nothing to clean up right now.");
        return queued;
    }

    /// <summary>One scheduler pass: queue the nightly run when it is due, then execute queued runs and close stale ones.</summary>
    public async Task ProcessAsync(DateTime now, CancellationToken token)
    {
        var settings = await SettingsAsync(token);
        var slot = now.Date.AddMinutes(settings.MinutesAfterMidnightUtc);
        // A slot chosen after it passed today waits for tomorrow; a slot missed by a restart still runs within the grace period.
        if (slot <= now && slot > settings.UpdatedAtUtc && now - slot < MissedSlotGrace
            && !await db.ServerCleanupRuns.AnyAsync(run => run.Trigger == ServerCleanupTriggers.Scheduled && run.StartedAtUtc >= slot, token))
        {
            if (await QueueAsync(ServerCleanupTriggers.Scheduled, now, token) == 0)
                db.ServerCleanupRuns.Add(new ServerCleanupRun
                {
                    Id = Guid.NewGuid(), RunnerName = "-", StartedAtUtc = now, FinishedAtUtc = now, Result = ServerCleanupResults.Failed,
                    Trigger = ServerCleanupTriggers.Scheduled, Message = "No server Runner was online at the scheduled time."
                });
            await db.SaveChangesAsync(token);
        }
        // A run left "running" by a restart can never report back.
        var stale = now - RunLimit - RunLimit;
        foreach (var run in await db.ServerCleanupRuns.Where(run => run.Result == ServerCleanupResults.Running && run.StartedAtUtc < stale).ToListAsync(token))
        {
            run.Result = ServerCleanupResults.Failed; run.FinishedAtUtc = now; run.Message = "The server restarted before the cleanup reported back.";
        }
        db.ServerCleanupRuns.RemoveRange(await db.ServerCleanupRuns.Where(run => run.StartedAtUtc < now - History).ToListAsync(token));
        await db.SaveChangesAsync(token);

        foreach (var id in await db.ServerCleanupRuns.Where(run => run.Result == Queued).OrderBy(run => run.StartedAtUtc).Select(run => run.Id).ToListAsync(token))
            await ExecuteAsync(id, settings, token);
    }

    private async Task ExecuteAsync(Guid id, ServerCleanupSettings settings, CancellationToken token)
    {
        var run = await db.ServerCleanupRuns.SingleAsync(item => item.Id == id, token);
        run.Result = ServerCleanupResults.Running;
        run.StartedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(RunLimit);
        try
        {
            var result = await runner.SendAsync<HostCleanupResult>(run.RunnerId, "host-cleanup",
                new HostCleanupRequest(settings.KeepDays, settings.JournalLimitMb), limit.Token);
            run.Result = ServerCleanupResults.Succeeded; run.BytesFreed = result.BytesFreed; run.Message = Clip(result.Summary);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            run.Result = ServerCleanupResults.Failed;
            run.Message = Clip(exception is OperationCanceledException ? "The Runner did not finish the cleanup in time." : exception.Message);
        }
        run.FinishedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<int> QueueAsync(string trigger, DateTime now, CancellationToken token)
    {
        var hosts = (await db.Runners.AsNoTracking()
            .Where(item => item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.DeletionRequestedAtUtc == null
                && item.InstallationId != null && item.InstallationId != "")
            .Select(item => new { item.Id, item.Name, item.ConnectionId, item.LastHeartbeatAtUtc })
            .ToListAsync(token))
            .Where(item => RunnerPresenceEvaluator.IsOnline(item.ConnectionId, item.LastHeartbeatAtUtc, now)).ToList();
        foreach (var host in hosts)
        {
            if (await db.ServerCleanupRuns.AnyAsync(run => run.RunnerId == host.Id && (run.Result == Queued || run.Result == ServerCleanupResults.Running), token))
                continue; // one cleanup at a time per host
            db.ServerCleanupRuns.Add(new ServerCleanupRun
            {
                Id = Guid.NewGuid(), RunnerId = host.Id, RunnerName = host.Name, StartedAtUtc = now, Result = Queued, Trigger = trigger
            });
        }
        await db.SaveChangesAsync(token);
        return hosts.Count;
    }

    private async Task<ServerCleanupSettings> SettingsAsync(CancellationToken token)
    {
        var settings = await db.ServerCleanupSettings.SingleOrDefaultAsync(token);
        if (settings is not null) return settings;
        settings = new ServerCleanupSettings { UpdatedAtUtc = DateTime.UtcNow };
        db.ServerCleanupSettings.Add(settings);
        await db.SaveChangesAsync(token);
        return settings;
    }

    internal static DateTime NextSlot(ServerCleanupSettings settings, DateTime now)
    {
        var today = now.Date.AddMinutes(settings.MinutesAfterMidnightUtc);
        return today > now ? today : today.AddDays(1);
    }

    private static ServerCleanupSettingsDto ToDto(ServerCleanupSettings settings)
        => new($"{settings.MinutesAfterMidnightUtc / 60:00}:{settings.MinutesAfterMidnightUtc % 60:00}", settings.KeepDays, settings.JournalLimitMb);

    private static string Clip(string text) => text.Length <= 1900 ? text : text[..1900];
}
