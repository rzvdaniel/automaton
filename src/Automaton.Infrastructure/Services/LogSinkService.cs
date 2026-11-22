using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Core.Logs;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Api.Services;

/// <summary>
/// Which installed app receives the server's logs (<c>logs:receive</c>) and where it listens. The Runner that hosts the
/// app reports its runtimes; the server keeps the address of the approved one in a setting, and the API and Worker read
/// it. Pausing, uninstalling or revoking the permission ends it; so does a Runner that stops reporting.
/// </summary>
public sealed class LogSinkService(ApplicationDbContext db, TimeProvider? clock = null, ILogger<LogSinkService>? logger = null)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> WarnedRunners = new();
    public const string SettingKey = "Logging.Sink";

    /// <summary>A report older than this no longer counts: the Runner reports every 30 seconds.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(3);

    private const int MaxReports = 200;
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    private sealed record Stored(Guid AppId, Guid DeploymentTargetId, Guid RunnerId, string Address, DateTime ReportedAtUtc);

    /// <summary>
    /// Called by a Runner with the app runtimes it hosts. <paramref name="runnerHost"/> is the host address the server
    /// already verified for this Runner (its probed gateway); only an address on that host is accepted, so a Runner
    /// cannot point the server's logs at another machine. Records the approved log-receiving app (if any) and returns
    /// it, or clears this Runner's earlier record when there is none.
    /// </summary>
    public async Task<LogSinkAssignment?> SyncAsync(Guid runnerId, string? runnerHost, IReadOnlyList<LogSinkRuntimeReport>? running, CancellationToken token)
    {
        var reports = (running ?? []).Take(MaxReports)
            .Select(report => (Report: report, Address: NormalizeAddress(report.Address, runnerHost)))
            .Where(item => item.Address is not null)
            .OrderBy(item => item.Report.AppId).ThenBy(item => item.Report.DeploymentTargetId)
            .ToList();

        WarnWhenAllDropped(runnerId, runnerHost, running?.Count ?? 0, reports.Count);

        var existing = await db.Settings.SingleOrDefaultAsync(setting => setting.Key == SettingKey, token);
        var stored = existing is null ? null : Read(existing);
        LogSinkAssignment? chosen = await ChooseAsync(runnerId, reports.Select(item => (item.Report, item.Address!)).ToList(), token);
        // Two Runners hosting receivers: the one already serving keeps it while it is healthy.
        if (chosen is not null && stored is not null && stored.RunnerId != runnerId && IsFresh(stored)
            && await IsReceiverAsync(stored.AppId, stored.DeploymentTargetId, stored.RunnerId, token))
            return null;

        if (chosen is not null) await StoreAsync(existing, stored, runnerId, chosen, token);
        else if (existing is not null && stored?.RunnerId == runnerId)
        {
            db.Settings.Remove(existing);
            await db.SaveChangesAsync(token);
        }
        return chosen;
    }

    private void WarnWhenAllDropped(Guid runnerId, string? runnerHost, int reported, int kept)
    {
        if (reported > 0 && kept == 0 && runnerHost is not null && WarnedRunners.TryAdd(runnerId, true))
            logger?.LogWarning("Runner {RunnerId} reported {Count} runtimes for the log receiver, but none has a private http address on host {Host}; none is used", runnerId, reported, runnerHost);
        else if (kept > 0) WarnedRunners.TryRemove(runnerId, out _);
    }

    private async Task<LogSinkAssignment?> ChooseAsync(Guid runnerId, List<(LogSinkRuntimeReport Report, string Address)> reports, CancellationToken token)
    {
        if (reports.Count == 0) return null;
        var ids = reports.Select(item => item.Report.AppId).Distinct().ToList();
        var approved = await db.AppPermissionApprovals.AsNoTracking().Where(item => ids.Contains(item.AppId)
            && item.Permission == AppPermissionApproval.LogsReceive && item.Status == AppPermissionStatuses.Approved)
            .Select(item => item.AppId).ToListAsync(token);
        foreach (var (report, address) in reports.Where(item => approved.Contains(item.Report.AppId)))
        {
            if (await IsReceiverAsync(report.AppId, report.DeploymentTargetId, runnerId, token))
                return new LogSinkAssignment(report.AppId, report.DeploymentTargetId, address);
        }
        return null;
    }

    private async Task StoreAsync(Setting? existing, Stored? stored, Guid runnerId, LogSinkAssignment chosen, CancellationToken token)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // A Runner reports every 30 seconds; the record is rewritten only when it changed or is about to look stale.
        var unchanged = stored is not null && stored.RunnerId == runnerId && stored.AppId == chosen.AppId
            && stored.DeploymentTargetId == chosen.DeploymentTargetId && stored.Address == chosen.Address
            && now - stored.ReportedAtUtc < Freshness / 3;
        if (unchanged) return;
        var value = JsonSerializer.Serialize(new Stored(chosen.AppId, chosen.DeploymentTargetId, runnerId, chosen.Address, now));
        if (existing is null)
            db.Settings.Add(new Setting { Key = SettingKey, Value = value, Description = "The app that receives the server's logs (logs:receive)." });
        else
        {
            existing.Value = value;
            existing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(token);
    }

    private bool IsFresh(Stored stored) => clock.GetUtcNow().UtcDateTime - stored.ReportedAtUtc <= Freshness;

    /// <summary>The address to ship logs to now, or null when no approved, running, unpaused app receives them.</summary>
    public async Task<string?> ResolveAddressAsync(CancellationToken token)
    {
        var setting = await db.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == SettingKey, token);
        if (setting is null || Read(setting) is not { } stored) return null;
        if (!IsFresh(stored)) return null;
        return await IsReceiverAsync(stored.AppId, stored.DeploymentTargetId, stored.RunnerId, token) ? stored.Address : null;
    }

    private async Task<bool> IsReceiverAsync(Guid appId, Guid targetId, Guid runnerId, CancellationToken token)
    {
        var app = await db.WebApps.AsNoTracking().Where(item => item.Id == appId)
            .Select(item => new { item.IsPaused, item.AccessStatus, item.ApplicationContractJson }).SingleOrDefaultAsync(token);
        if (app is null || app.IsPaused || app.AccessStatus != AppAccessStatuses.Active) return false;
        if (!RequestedPermissions(app.ApplicationContractJson).Contains(AppPermissionApproval.LogsReceive, StringComparer.Ordinal)) return false;
        var approved = await db.AppPermissionApprovals.AsNoTracking().AnyAsync(item => item.AppId == appId
            && item.Permission == AppPermissionApproval.LogsReceive && item.Status == AppPermissionStatuses.Approved, token);
        return approved && await db.AppDeploymentTargets.AsNoTracking()
            .AnyAsync(item => item.Id == targetId && item.WebAppId == appId && item.ExecutionRunnerId == runnerId, token);
    }

    private static string[] RequestedPermissions(string json)
    {
        try { return ApplicationContractSerialization.Deserialize(json).Permissions.ToArray(); }
        catch (InvalidOperationException) { return []; }
        catch (JsonException) { return []; }
    }

    private static Stored? Read(Setting setting)
    {
        try { return JsonSerializer.Deserialize<Stored>(setting.Value); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Only a plain http address on <paramref name="requiredHost"/> (the Runner's own verified host) with a private IPv4
    /// literal and a port: the server never sends logs to names, public hosts or other machines a Runner chose.
    /// </summary>
    public static string? NormalizeAddress(string? address, string? requiredHost)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp) return null;
        if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.UserInfo.Length > 0) return null;
        if (uri.IsDefaultPort || uri.Port < 1024) return null;
        if (!IPAddress.TryParse(uri.Host, out var ip) || !IsPrivateIPv4(ip)) return null;
        return requiredHost is not null && ip.ToString() == requiredHost ? $"http://{ip}:{uri.Port}" : null;
    }

    private static bool IsPrivateIPv4(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
}

/// <summary>Keeps the API's or Worker's log shipping pointed at the app that receives logs, when the admin set no endpoint.</summary>
public sealed class LogForwardingService(IServiceScopeFactory scopes, ILogger<LogForwardingService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!LogForwarding.IsSwitchable) return;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                LogForwarding.SetAddress(await scope.ServiceProvider.GetRequiredService<LogSinkService>().ResolveAddressAsync(stoppingToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Keep the last address; a database that is busy or not migrated yet must not fill the log.
                logger.LogDebug(exception, "Could not read the log receiver");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }
}

public static class LogForwardingRegistration
{
    public static IServiceCollection AddAutomatonLogForwarding(this IServiceCollection services)
    {
        services.AddScoped<LogSinkService>(provider => new LogSinkService(provider.GetRequiredService<ApplicationDbContext>(), null, provider.GetService<ILogger<LogSinkService>>()));
        services.AddHostedService<LogForwardingService>();
        return services;
    }
}
