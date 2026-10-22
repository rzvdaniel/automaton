using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Automaton.Runner.Console.Services;

public sealed class StorageTelemetryBackgroundService(RunnerStorageTelemetryApiClient telemetryClient) : BackgroundService
{
    private readonly SemaphoreSlim measurementLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var purpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        if (purpose != RunnerPurposes.Managed ||
            !string.Equals(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"), RunnerApplicationProfiles.MachineManagement, StringComparison.OrdinalIgnoreCase))
            return;
        await Task.Delay(GetInitialDelay(Random.Shared), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = await MeasureAndReportAsync(purpose, stoppingToken);
            var delay = GetNextDelay(succeeded, Random.Shared);
            await Task.Delay(delay, stoppingToken);
        }
    }

    public static TimeSpan GetInitialDelay(Random random) => TimeSpan.FromMinutes(random.Next(2, 11));

    public static TimeSpan GetNextDelay(bool succeeded, Random random) =>
        succeeded
            ? TimeSpan.FromMinutes(60 + random.Next(-5, 6))
            : TimeSpan.FromMinutes(random.Next(10, 16));

    internal async Task<bool> MeasureAndReportAsync(string purpose, CancellationToken stoppingToken)
    {
        if (!await measurementLock.WaitAsync(0, stoppingToken)) return false;
        try
        {
            if (purpose == RunnerPurposes.Managed && string.Equals(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"), RunnerApplicationProfiles.MachineManagement, StringComparison.OrdinalIgnoreCase))
                return await MeasureMachineRunnerAsync(stoppingToken);
            return true;
        }
        finally { measurementLock.Release(); }
    }

    private async Task<bool> MeasureMachineRunnerAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_INSTALLATION_ID")))
            return false;

        var measuredAt = DateTime.UtcNow;
        try
        {
            var capacity = ReadFilesystemCapacity("/automaton/storage");

            await telemetryClient.ReportMachineRunnerAsync(new(
                measuredAt,
                capacity.TotalBytes,
                capacity.AvailableBytes,
                true,
                null,
                typeof(StorageTelemetryBackgroundService).Assembly.GetName().Version?.ToString()), stoppingToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await telemetryClient.ReportMachineRunnerAsync(new(
                    measuredAt, 0, 0, false, ex.Message,
                    typeof(StorageTelemetryBackgroundService).Assembly.GetName().Version?.ToString()), stoppingToken);
            }
            catch (Exception reportError) when (!stoppingToken.IsCancellationRequested)
            {
                Log.Warning(reportError, "Could not report Machine Management Runner storage measurement failure.");
            }
            return false;
        }
    }

    internal static (long TotalBytes, long AvailableBytes) ReadFilesystemCapacity(string path)
    {
        var volume = new DriveInfo(path);
        if (!volume.IsReady || volume.TotalSize <= 0 || volume.AvailableFreeSpace < 0 || volume.AvailableFreeSpace > volume.TotalSize)
            throw new InvalidOperationException("The Machine Management workspace filesystem returned invalid capacity.");
        return (volume.TotalSize, volume.AvailableFreeSpace);
    }


}
