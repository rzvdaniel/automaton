using NUnit.Framework;
using Automaton.Worker.Services;

namespace Automaton.Worker.Tests;

public sealed class RetiredStorageScheduleCleanupTests
{
    [TestCase(RetiredStorageScheduleKeys.Enforcement, false)]
    [TestCase(RetiredStorageScheduleKeys.Enforcement, true)]
    [TestCase(RetiredStorageScheduleKeys.HostedRunner, false)]
    [TestCase(RetiredStorageScheduleKeys.HostedRunner, true)]
    [TestCase(RetiredStorageScheduleKeys.RunnerHost, false)]
    [TestCase(RetiredStorageScheduleKeys.RunnerHost, true)]
    public async Task StartAsync_DisablesRetiredSystemSchedulesWithoutChangingUserSchedules(string key, bool legacyIdentity)
    {
        await using var fixture = await RetiredStorageScheduleCleanupFixture.CreateAsync();
        await fixture.SeedRetiredScheduleAsync(key, legacyIdentity);
        await fixture.SeedTwiceAsync();
        await fixture.AssertRetiredScheduleDisabledAsync(key);
    }

}
