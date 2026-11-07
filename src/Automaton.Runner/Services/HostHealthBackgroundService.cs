using Automaton.Core.Enums;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Automaton.Runner.Console.Services;

/// <summary>Reports host figures every few minutes from a Machine Management Runner.</summary>
public sealed class HostHealthBackgroundService(
    HostHealthCollector collector,
    RunnerStorageTelemetryApiClient reportClient) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static bool IsMachineManagementRunner() =>
        RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE")) == RunnerPurposes.Managed
        && string.Equals(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"),
            RunnerApplicationProfiles.MachineManagement, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID"));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsMachineManagementRunner()) return;
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await reportClient.ReportHostHealthAsync(await collector.CollectAsync(stoppingToken), stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                Log.Warning(exception, "Could not report Machine Management host health.");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }
}
