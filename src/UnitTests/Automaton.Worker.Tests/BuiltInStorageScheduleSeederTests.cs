using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class BuiltInStorageScheduleSeederTests
{
    [Test]
    public async Task StartAsync_IsIdempotentAndSeedsBothWorkerSchedules()
    {
        await using var fixture = await BuiltInStorageScheduleSeederFixture.CreateAsync();
        await fixture.SeedTwiceAsync();
        await fixture.AssertSeededSchedulesAsync();
    }

    [Test]
    public async Task StartAsync_ReconcilesImmutableDefinitionWithoutDuplicatingSchedules()
    {
        await using var fixture = await BuiltInStorageScheduleSeederFixture.CreateAsync();
        await fixture.SeedOnceAsync();
        await fixture.TamperHostedRunnerScheduleAsync();
        await fixture.SeedOnceAsync();
        await fixture.AssertHostedRunnerScheduleWasReconciledAsync();
    }
}
