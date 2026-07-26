using Automaton.Api.Models;
using Automaton.Api.Services;

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
    public void RequiredHostReserve_UsesGreaterOfFifteenGiBAndFifteenPercent()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StorageTelemetryPolicy.RequiredHostReserve(50L * 1024 * 1024 * 1024),
                Is.EqualTo(15L * 1024 * 1024 * 1024));
            Assert.That(StorageTelemetryPolicy.RequiredHostReserve(200L * 1024 * 1024 * 1024),
                Is.EqualTo(30L * 1024 * 1024 * 1024));
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
}
