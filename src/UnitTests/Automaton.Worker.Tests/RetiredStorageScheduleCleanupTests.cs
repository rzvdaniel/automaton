using NUnit.Framework;
using Automaton.Worker.Services;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Worker.Tests;

public sealed class RetiredStorageScheduleCleanupTests
{
    [Test]
    public async Task StartAsync_UsesRelationalBulkCleanupAndPreservesQueuedWork()
    {
        await using var fixture = await RetiredStorageScheduleCleanupFixture.CreateRelationalAsync();
        await fixture.SeedRelationalAsync(RetiredStorageScheduleKeys.Enforcement);
        await fixture.SeedOnceAsync();
        await fixture.AssertRelationalCleanupAsync();
    }

    [TestCase(RetiredStorageScheduleKeys.Enforcement, false)]
    [TestCase(RetiredStorageScheduleKeys.Enforcement, true)]
    [TestCase(RetiredStorageScheduleKeys.HostedRunner, false)]
    [TestCase(RetiredStorageScheduleKeys.HostedRunner, true)]
    [TestCase(RetiredStorageScheduleKeys.RunnerHost, false)]
    [TestCase(RetiredStorageScheduleKeys.RunnerHost, true)]
    public async Task StartAsync_DeletesRetiredSystemSchedulesAndRelatedDataWithoutChangingUserSchedules(string key, bool legacyIdentity)
    {
        await using var fixture = await RetiredStorageScheduleCleanupFixture.CreateAsync();
        await fixture.SeedRetiredScheduleAsync(key, legacyIdentity);
        await fixture.SeedTwiceAsync();
        await fixture.AssertRetiredScheduleDeletedAsync(key);
    }

}
