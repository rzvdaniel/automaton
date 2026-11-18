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
public sealed class LogSinkService(ApplicationDbContext db, TimeProvider? clock = null)
{
    public const string SettingKey = "Logging.Sink";

    /// <summary>A report older than this no longer counts: the Runner reports every 30 seconds.</summary>
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(3);

    private const int MaxReports = 200;
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    private sealed record Stored(Guid AppId, Guid DeploymentTargetId, Guid RunnerId, string Address, DateTime ReportedAtUtc);

    /// <summary>
    /// Called by a Runner with the app runtimes it hosts. Records the approved log-receiving one (if any) and returns it,
    /// or clears this Runner's earlier record when there is none.
    /// </summary>
    public async Task<LogSinkAssignment?> SyncAsync(Guid runnerId, IReadOnlyList<LogSinkRuntimeReport>? running, CancellationToken token)
    {
        var reports = (running ?? []).Take(MaxReports)
            .Select(report => (Report: report, Address: NormalizeAddress(report.Address)))
            .Where(item => item.Address is not null)
            .OrderBy(item => item.Report.AppId).ThenBy(item => item.Report.DeploymentTargetId)
            .ToList();

        var existing = await db.Settings.SingleOrDefaultAsync(setting => setting.Key == SettingKey, token);
        LogSinkAssignment? chosen = null;
        foreach (var item in reports)
        {
            if (await IsReceiverAsync(item.Report.AppId, item.Report.DeploymentTargetId, runnerId, token))
            {
                chosen = new LogSinkAssignment(item.Report.AppId, item.Report.DeploymentTargetId, item.Address!);
                break;
            }
        }

        if (chosen is not null)
        {
            var value = JsonSerializer.Serialize(new Stored(chosen.AppId, chosen.DeploymentTargetId, runnerId, chosen.Address, clock.GetUtcNow().UtcDateTime));
            if (existing is null)
                db.Settings.Add(new Setting { Key = SettingKey, Value = value, Description = "The app that receives the server's logs (logs:receive)." });
            else
            {
                existing.Value = value;
                existing.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            }
            await db.SaveChangesAsync(token);
        }
        else if (existing is not null && Read(existing) is { } stored && stored.RunnerId == runnerId)
        {
            db.Settings.Remove(existing);
            await db.SaveChangesAsync(token);
        }
        return chosen;
    }

    /// <summary>The address to ship logs to now, or null when no approved, running, unpaused app receives them.</summary>
    public async Task<string?> ResolveAddressAsync(CancellationToken token)
    {
        var setting = await db.Settings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == SettingKey, token);
        if (setting is null || Read(setting) is not { } stored) return null;
        if (clock.GetUtcNow().UtcDateTime - stored.ReportedAtUtc > Freshness) return null;
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
    /// Only a plain http address with a private IPv4 literal and a port: the server never sends logs to names, public
    /// hosts or paths a Runner chose.
    /// </summary>
    public static string? NormalizeAddress(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp) return null;
        if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.UserInfo.Length > 0) return null;
        if (uri.IsDefaultPort || uri.Port < 1024) return null;
        return IPAddress.TryParse(uri.Host, out var ip) && IsPrivateIPv4(ip) ? $"http://{ip}:{uri.Port}" : null;
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
        services.AddScoped<LogSinkService>(provider => new LogSinkService(provider.GetRequiredService<ApplicationDbContext>()));
        services.AddHostedService<LogForwardingService>();
        return services;
    }
}
