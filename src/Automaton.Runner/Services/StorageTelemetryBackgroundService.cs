using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Diagnostics;
using System.Globalization;

namespace Automaton.Runner.Console.Services;

public sealed class StorageTelemetryBackgroundService(HubService hubService) : BackgroundService
{
    private static readonly string[] ManagedRoots =
    [
        "/automaton/system",
        "/automaton/storage/workspace",
        "/automaton/storage/logs",
        "/automaton/storage/home"
    ];
    private readonly SemaphoreSlim measurementLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var purpose = RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"));
        await Task.Delay(GetInitialDelay(purpose, Random.Shared), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = await MeasureAndReportAsync(purpose, stoppingToken);
            var delay = GetNextDelay(purpose, succeeded, Random.Shared);
            await Task.Delay(delay, stoppingToken);
        }
    }

    public static TimeSpan GetInitialDelay(string purpose, Random random) =>
        TimeSpan.FromMinutes(purpose == RunnerPurposes.Infrastructure ? random.Next(1, 6) : random.Next(2, 11));

    public static TimeSpan GetNextDelay(string purpose, bool succeeded, Random random) =>
        succeeded
            ? TimeSpan.FromMinutes(60 + random.Next(-5, 6))
            : TimeSpan.FromMinutes(purpose == RunnerPurposes.Infrastructure ? 10 : random.Next(10, 16));

    internal async Task<bool> MeasureAndReportAsync(string purpose, CancellationToken stoppingToken)
    {
        if (!await measurementLock.WaitAsync(0, stoppingToken)) return false;
        try
        {
            return purpose == RunnerPurposes.Infrastructure
                ? await MeasureHostAsync(stoppingToken)
                : await MeasureHostedRunnerAsync(stoppingToken);
        }
        finally { measurementLock.Release(); }
    }

    private async Task<bool> MeasureHostedRunnerAsync(CancellationToken stoppingToken)
    {
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID"), out var hostedRunnerId))
            return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var values = new long[ManagedRoots.Length];
            for (var i = 0; i < ManagedRoots.Length; i++)
                values[i] = await StorageMeasurement.MeasureDirectoryAsync(ManagedRoots[i], timeout.Token);
            var user = checked(values[1] + values[2] + values[3]);
            await hubService.ReportHostedRunnerStorageAsync(new(
                hostedRunnerId, DateTime.UtcNow, values[0], values[1], values[2], values[3],
                user, checked(values[0] + user), stopwatch.ElapsedMilliseconds, true, null,
                typeof(StorageTelemetryBackgroundService).Assembly.GetName().Version?.ToString()), stoppingToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await hubService.ReportHostedRunnerStorageAsync(new(
                    hostedRunnerId, DateTime.UtcNow, 0, 0, 0, 0, 0, 0, stopwatch.ElapsedMilliseconds,
                    false, ex.Message, typeof(StorageTelemetryBackgroundService).Assembly.GetName().Version?.ToString()), stoppingToken);
            }
            catch (Exception reportError) when (!stoppingToken.IsCancellationRequested)
            {
                Log.Warning(reportError, "Could not report hosted Runner storage measurement failure.");
            }
            return false;
        }
    }

    private async Task<bool> MeasureHostAsync(CancellationToken stoppingToken)
    {
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_HOST_ID"), out var hostId))
            return false;
        try
        {
            var dockerRootResult = await ExecuteHostCommandAsync(["docker", "info", "--format", "{{.DockerRootDir}}"], stoppingToken);
            var dockerRoot = ParseDockerRoot(dockerRootResult);
            await RunnerHostImageStore.VerifyBoundedLayoutAsync(dockerRoot, ExecuteHostCommandAsync, stoppingToken);
            await TryPruneUnusedImagesAsync(stoppingToken);
            var hostFilesystem = ParseDf(await ExecuteHostCommandAsync(["df", "-B1", "-P", "/"], stoppingToken));
            var dockerFilesystem = ParseDf(await ExecuteHostCommandAsync(["df", "-B1", "-P", dockerRoot], stoppingToken));
            await hubService.ReportRunnerHostStorageAsync(new(
                hostId, DateTime.UtcNow, hostFilesystem.Path, hostFilesystem.TotalBytes, hostFilesystem.UsedBytes, hostFilesystem.AvailableBytes,
                dockerRoot, dockerFilesystem.TotalBytes, dockerFilesystem.AvailableBytes, true, null,
                typeof(StorageTelemetryBackgroundService).Assembly.GetName().Version?.ToString(), true), stoppingToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await hubService.ReportRunnerHostStorageAsync(new(
                    hostId, DateTime.UtcNow, string.Empty, 0, 0, 0, null, null, null, false,
                    ex.Message, typeof(StorageTelemetryBackgroundService).Assembly.GetName().Version?.ToString()), stoppingToken);
            }
            catch (Exception reportError) when (!stoppingToken.IsCancellationRequested)
            {
                Log.Warning(reportError, "Could not report Runner Host storage measurement failure.");
            }
            return false;
        }
    }

    public sealed record FilesystemMeasurement(string Path, long TotalBytes, long UsedBytes, long AvailableBytes);

    public static string ParseDockerRoot(string output)
    {
        var root = output.Trim();
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root) || root.Contains('\0'))
            throw new InvalidOperationException("Docker did not return an absolute data-root path.");
        return root;
    }

    public static FilesystemMeasurement ParseDf(string output)
    {
        var line = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? throw new InvalidOperationException("Host returned no filesystem measurement.");
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 5 || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var total)
            || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var used)
            || !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var available)
            || total < 0 || used < 0 || available < 0 || used > total || available > total)
            throw new InvalidOperationException("Host returned invalid filesystem measurement.");
        return new FilesystemMeasurement(fields[^1], total, used, available);
    }

    private static async Task<string> ExecuteHostCommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("automaton-host-exec") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start host command bridge.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Trim() is { Length: > 0 } error ? error : "Host command failed.");
        return stdout;
    }

    private static async Task TryPruneUnusedImagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteHostCommandAsync(
                ["docker", "image", "prune", "--all", "--force", "--filter", "until=24h"], cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warning(ex, "Could not prune unused Docker images on the Runner Host.");
        }
    }
}
