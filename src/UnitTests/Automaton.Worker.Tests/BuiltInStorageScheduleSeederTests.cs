using NUnit.Framework;
using Automaton.Worker.Services;

namespace Automaton.Worker.Tests;

public sealed class BuiltInStorageScheduleSeederTests
{
    [TestCase(BuiltInStorageScheduleActionKeys.RetiredEnforcement, false)]
    [TestCase(BuiltInStorageScheduleActionKeys.RetiredEnforcement, true)]
    [TestCase(BuiltInStorageScheduleActionKeys.HostedRunner, false)]
    [TestCase(BuiltInStorageScheduleActionKeys.HostedRunner, true)]
    public async Task StartAsync_DisablesRetiredSystemSchedulesWithoutChangingUserSchedules(string key, bool legacyIdentity)
    {
        await using var fixture = await BuiltInStorageScheduleSeederFixture.CreateAsync();
        await fixture.SeedRetiredScheduleAsync(key, legacyIdentity);
        await fixture.SeedTwiceAsync();
        await fixture.AssertRetiredScheduleDisabledAsync(key);
    }

    [Test]
    public async Task StartAsync_IsIdempotentAndSeedsOnlyHostWorkerSchedule()
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
        await fixture.TamperRunnerHostScheduleAsync();
        await fixture.SeedOnceAsync();
        await fixture.AssertRunnerHostScheduleWasReconciledAsync();
    }
}
