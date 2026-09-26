using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Entities;

namespace Automaton.Api.Tests;

public class StorageTelemetryPolicyTests
{
    [TestCase(79, HostedRunnerStorageStates.Healthy)]
    [TestCase(80, HostedRunnerStorageStates.Warning)]
    [TestCase(90, HostedRunnerStorageStates.DeploymentBlocked)]
    [TestCase(100, HostedRunnerStorageStates.DeploymentBlocked)]
    [TestCase(101, HostedRunnerStorageStates.OverLimit)]
    public void EvaluateHostedRunner_UsesConfiguredStageFiveBoundaries(int percent, string expected)
    {
        Assert.That(StorageTelemetryPolicy.EvaluateHostedRunner(100, percent, true), Is.EqualTo(expected));
    }

    [Test]
    public void EvaluateHostedRunner_FailureIsNeverHealthyOrZero()
    {
        Assert.That(StorageTelemetryPolicy.EvaluateHostedRunner(100, 0, false),
            Is.EqualTo(HostedRunnerStorageStates.MeasurementFailed));
    }

    [Test]
    public void RequiredHostReserve_UsesGreaterOfFiveGiBAndFifteenPercent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StorageTelemetryPolicy.RequiredHostReserve(20L * 1024 * 1024 * 1024),
                Is.EqualTo(5L * 1024 * 1024 * 1024));
            Assert.That(StorageTelemetryPolicy.RequiredHostReserve(50L * 1024 * 1024 * 1024),
                Is.EqualTo(7.5m * 1024 * 1024 * 1024));
            Assert.That(StorageTelemetryPolicy.RequiredHostReserve(200L * 1024 * 1024 * 1024),
                Is.EqualTo(30L * 1024 * 1024 * 1024));
        });
    }

    [Test]
    public void CanScheduleHostedRunners_SubtractsRequestedStorageBeforeApplyingReserve()
    {
        var reserve = 5L * 1024 * 1024 * 1024;

        Assert.Multiple(() =>
        {
            Assert.That(StorageTelemetryPolicy.CanScheduleHostedRunners(
                RunnerHostStorageStates.Healthy, reserve + 2L * 1024 * 1024 * 1024 - 1,
                reserve, 2L * 1024 * 1024 * 1024), Is.False);
            Assert.That(StorageTelemetryPolicy.CanScheduleHostedRunners(
                RunnerHostStorageStates.Healthy, reserve + 2L * 1024 * 1024 * 1024,
                reserve, 2L * 1024 * 1024 * 1024), Is.True);
        });
    }

    [Test]
    public void EvaluateHostedRunner_UsesConfiguredThresholds()
    {
        var options = new StorageTelemetryOptions
        {
            WarningPercent = 70,
            DeploymentBlockPercent = 85,
            OverLimitPercent = 95
        };
        Assert.That(StorageTelemetryPolicy.EvaluateHostedRunner(100, 86, true, options),
            Is.EqualTo(HostedRunnerStorageStates.DeploymentBlocked));
    }

    [TestCase(RunnerHostStorageStates.Stale)]
    [TestCase(RunnerHostStorageStates.MeasurementFailed)]
    [TestCase(RunnerHostStorageStates.Unknown)]
    [TestCase(RunnerHostStorageStates.SchedulingBlocked)]
    [TestCase(RunnerHostStorageStates.Critical)]
    public void CanScheduleHostedRunners_BlocksIncompleteHostTelemetry(string state)
    {
        Assert.That(StorageTelemetryPolicy.CanScheduleHostedRunners(state, 1000, 100, 10), Is.False);
    }

    [Test]
    public void CanScheduleHostedRunners_UsesActualAvailableBytesAfterReservation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StorageTelemetryPolicy.CanScheduleHostedRunners(RunnerHostStorageStates.Healthy, 109, 100, 10), Is.False);
            Assert.That(StorageTelemetryPolicy.CanScheduleHostedRunners(RunnerHostStorageStates.Healthy, 110, 100, 10), Is.True);
        });
    }

    [Test]
    public void CanRunDockerStorageOperation_RequiresFreshTelemetryAndHostReserve()
    {
        var now = DateTime.UtcNow;
        var host = new RunnerHost
        {
            StorageState = RunnerHostStorageStates.Healthy,
            StorageAvailableBytes = 110,
            StorageReserveRequiredBytes = 100,
            StorageMeasuredAtUtc = now
        };
        var options = new StorageTelemetryOptions { FreshAfterMinutes = 10 };

        Assert.Multiple(() =>
        {
            Assert.That(StorageTelemetryPolicy.CanRunDockerStorageOperation(host, now, options), Is.True);
            Assert.That(StorageTelemetryPolicy.CanRunDockerStorageOperation(host, now.AddMinutes(11), options), Is.False);
            host.StorageMeasuredAtUtc = now;
            host.StorageAvailableBytes = 99;
            Assert.That(StorageTelemetryPolicy.CanRunDockerStorageOperation(host, now, options), Is.False);
        });
    }
}
