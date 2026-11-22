using Automaton.Api.Entities;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppListNextActionRunTests
{
    [Test]
    public async Task ActionsOnlyAppShowsTheSoonestEnabledScheduleRun()
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync(successfulCleanup: true);
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        var soon = new DateTime(2026, 11, 9, 2, 0, 0, DateTimeKind.Utc);
        fixture.App.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract());
        AddSchedule(fixture, soon.AddDays(1), enabled: true);
        AddSchedule(fixture, soon, enabled: true);
        AddSchedule(fixture, soon.AddHours(-5), enabled: false);
        AddSchedule(fixture, null, enabled: true);
        await fixture.Db.SaveChangesAsync();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(fixture.Db, fixture.UserId, directory.Path, out _);

        var app = (await service.ListAsync(cancellationToken: CancellationToken.None)).Single(item => item.Id == fixture.App.Id);

        Assert.That(app.ActionsOnly, Is.True);
        Assert.That(app.NextActionRunAtUtc, Is.EqualTo(soon));
    }

    [Test]
    public async Task ActionsOnlyAppWithoutAnEnabledScheduleHasNoNextRun()
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync(successfulCleanup: true);
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        fixture.App.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract());
        AddSchedule(fixture, DateTime.UtcNow.AddHours(1), enabled: false);
        await fixture.Db.SaveChangesAsync();
        var service = AppAuthorizationTestServiceFactory.CreateAppsService(fixture.Db, fixture.UserId, directory.Path, out _);

        var app = (await service.ListAsync(cancellationToken: CancellationToken.None)).Single(item => item.Id == fixture.App.Id);

        Assert.That(app.NextActionRunAtUtc, Is.Null);
    }

    private static void AddSchedule(AppDeploymentTargetManagementServiceTests.TestFixtureContext fixture, DateTime? next, bool enabled)
        => fixture.Db.ActionSchedules.Add(new ActionSchedule
        {
            Id = Guid.NewGuid(), OwnerUserId = fixture.UserId, ApplicationId = fixture.App.Id, ActionKey = "backup",
            Name = "Nightly", IntervalSeconds = 300, Enabled = enabled, NextRunAtUtc = next
        });
}
