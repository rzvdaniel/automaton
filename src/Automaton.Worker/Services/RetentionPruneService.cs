using Automaton.Api.Data;
using Automaton.Api.Retention;

namespace Automaton.Worker.Services;

/// <summary>Once a night, deletes Action runs, logs and audit events that are past the retention rule.</summary>
public sealed class RetentionPruneService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionPruneService> logger) : BackgroundService
{
    /// <summary>Time of day (UTC) the job runs, after the 03:00 backup.</summary>
    internal static readonly TimeSpan RunAt = TimeSpan.FromHours(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DelayUntilNextRun(DateTime.UtcNow), stoppingToken);
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retention pruning failed");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal static TimeSpan DelayUntilNextRun(DateTime now)
    {
        var next = now.Date + RunAt;
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    internal async Task<RetentionReport> PruneAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var report = await new RetentionPruner(db).PruneAsync(DateTime.UtcNow, token);
        logger.LogInformation(
            "Retention removed {Runs} Action runs, expired {Logs} logs, removed {Occurrences} schedule occurrences and {Audit} audit events",
            report.Runs, report.LogsExpired, report.Occurrences, report.AuditEvents);
        return report;
    }
}
