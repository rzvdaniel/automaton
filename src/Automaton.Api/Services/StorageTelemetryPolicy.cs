using Automaton.Api.Models;
using Automaton.Api.Entities;

namespace Automaton.Api.Services;

public static class StorageTelemetryPolicy
{
    public static bool CanScheduleHostedRunners(string state, long? availableBytes, long reserveRequiredBytes, long requestedStorageBytes)
    {
        if (state == RunnerHostStorageStates.Healthy)
            return availableBytes.HasValue && availableBytes.Value - requestedStorageBytes >= reserveRequiredBytes;
        if (state == RunnerHostStorageStates.LowSpace)
            return availableBytes.HasValue && availableBytes.Value - requestedStorageBytes >= reserveRequiredBytes;
        return false;
    }

    public static bool CanScheduleHostedRunners(string state, long? availableBytes, long reserveRequiredBytes,
        long requestedStorageBytes, DateTime createdAtUtc, DateTime? measuredAtUtc, DateTime nowUtc, StorageTelemetryOptions options)
    {
        if (state == RunnerHostStorageStates.Unknown && !measuredAtUtc.HasValue &&
            createdAtUtc >= nowUtc.AddMinutes(-Math.Max(1, options.InitialTelemetryGraceMinutes)))
            return true;
        return CanScheduleHostedRunners(state, availableBytes, reserveRequiredBytes, requestedStorageBytes);
    }

    public static bool CanRunDockerStorageOperation(RunnerHost host, DateTime nowUtc, StorageTelemetryOptions options)
    {
        if (!host.StorageMeasuredAtUtc.HasValue)
        {
            return host.StorageState == RunnerHostStorageStates.Unknown
                && host.CreatedAt >= nowUtc.AddMinutes(-Math.Max(1, options.InitialTelemetryGraceMinutes));
        }

        if (host.StorageMeasuredAtUtc.Value < nowUtc.AddMinutes(-Math.Max(1, options.FreshAfterMinutes)))
            return false;

        return CanScheduleHostedRunners(
            host.StorageState,
            host.StorageAvailableBytes,
            host.StorageReserveRequiredBytes ?? long.MaxValue,
            requestedStorageBytes: 0);
    }

    public static string EvaluateHostedRunner(long allowanceBytes, long totalBytes, bool success, StorageTelemetryOptions? options = null)
    {
        options ??= new StorageTelemetryOptions();
        if (!success) return HostedRunnerStorageStates.MeasurementFailed;
        if (allowanceBytes <= 0) return HostedRunnerStorageStates.Unknown;
        var percent = totalBytes * 100m / allowanceBytes;
        if (percent > options.OverLimitPercent) return HostedRunnerStorageStates.OverLimit;
        if (percent >= options.DeploymentBlockPercent) return HostedRunnerStorageStates.DeploymentBlocked;
        if (percent >= options.WarningPercent) return HostedRunnerStorageStates.Warning;
        return HostedRunnerStorageStates.Healthy;
    }

    public static long RequiredHostReserve(long totalBytes, StorageTelemetryOptions? options = null)
    {
        options ??= new StorageTelemetryOptions();
        return Math.Max(options.MinimumHostReserveBytes, (long)Math.Ceiling(totalBytes * options.HostReservePercent / 100m));
    }

    public static string EvaluateHost(long totalBytes, long availableBytes, bool success, StorageTelemetryOptions? options = null)
    {
        if (!success) return RunnerHostStorageStates.MeasurementFailed;
        if (totalBytes <= 0) return RunnerHostStorageStates.Unknown;
        var reserve = RequiredHostReserve(totalBytes, options);
        if (availableBytes <= reserve / 2) return RunnerHostStorageStates.Critical;
        if (availableBytes < reserve) return RunnerHostStorageStates.SchedulingBlocked;
        if (availableBytes < reserve * 3 / 2) return RunnerHostStorageStates.LowSpace;
        return RunnerHostStorageStates.Healthy;
    }
}
