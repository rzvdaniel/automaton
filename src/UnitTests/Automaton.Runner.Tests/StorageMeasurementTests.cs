using Automaton.Runner.Console.Services;
using NUnit.Framework;
using Automaton.Core.Enums;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class StorageMeasurementTests
{
    [Test]
    public async Task MissingDirectory_IsSafelyMeasuredAsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.That(await StorageMeasurement.MeasureDirectoryAsync(path, default), Is.Zero);
    }

    [Test]
    public async Task ManagedDirectory_SumsRegularFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"automaton-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(path, "child"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(path, "one.bin"), new byte[17]);
            await File.WriteAllBytesAsync(Path.Combine(path, "child", "two.bin"), new byte[23]);
            Assert.That(await StorageMeasurement.MeasureDirectoryAsync(path, default), Is.EqualTo(40));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Test]
    public void CancelledTraversal_ReportsCancellation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"automaton-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(() =>
                StorageMeasurement.MeasureDirectoryAsync(path, cancellation.Token));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Test]
    public void TelemetrySchedule_AppliesPurposeSpecificInitialDelayAndJitter()
    {
        var random = new Random(42);
        for (var index = 0; index < 100; index++)
        {
            Assert.That(StorageTelemetryBackgroundService.GetInitialDelay(RunnerPurposes.Managed, random).TotalMinutes,
                Is.InRange(2, 10));
            Assert.That(StorageTelemetryBackgroundService.GetInitialDelay(RunnerPurposes.Infrastructure, random).TotalMinutes,
                Is.InRange(1, 5));
            Assert.That(StorageTelemetryBackgroundService.GetNextDelay(RunnerPurposes.Managed, true, random).TotalMinutes,
                Is.InRange(55, 65));
            Assert.That(StorageTelemetryBackgroundService.GetNextDelay(RunnerPurposes.Managed, false, random).TotalMinutes,
                Is.InRange(10, 15));
        }
    }
}
