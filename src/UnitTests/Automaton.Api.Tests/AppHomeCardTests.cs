using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Home;
using Automaton.Api.Models;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Apps show a status and alerts on their Studio Home card through Actions.</summary>
[TestFixture]
public sealed class AppHomeCardTests
{
    private const string Valid = """{"status":"All good","facts":[{"label":"Disk","value":"61%"}],"alerts":[{"severity":"warning","text":"Backup is late"}]}""";

    private static AppActionExecutionResult Result(string status, string? card, DateTime? at = null)
        => new() { Status = status, HomeCardJson = card, CompletedAtUtc = at ?? DateTime.UtcNow };

    private static async Task<Guid> RunAsync(ApplicationDbContext db, Guid appId, string key, string status, string? card, DateTime? at = null)
    {
        var id = Guid.NewGuid();
        db.AppModuleExecutions.Add(new AppModuleExecution { Id = id, AppId = appId, ModuleKey = key, ModuleType = "Action", Status = "Running", TriggerType = "Scheduled" });
        await db.SaveChangesAsync();
        await new AppHomeCardStore(db).ApplyRunAsync(id, Result(status, card, at), default);
        return id;
    }

    [Test]
    public void ValidCardIsParsedAndHtmlStaysText()
    {
        var card = AppHomeCardValidator.TryParse("""{"status":"<b>ok</b>","facts":[{"label":"<i>x</i>","value":"1"}]}""");
        Assert.That(card, Is.Not.Null);
        Assert.That(card!.Status, Is.EqualTo("<b>ok</b>"), "stored as plain text; Studio shows it as text");
        Assert.That(card.Alerts, Is.Empty);
    }

    [TestCase("""{"facts":[]}""", TestName = "NoStatus")]
    [TestCase("""{"status":""}""", TestName = "EmptyStatus")]
    [TestCase("""{"status":1}""", TestName = "StatusNotText")]
    [TestCase("""[1]""", TestName = "NotAnObject")]
    [TestCase("not json", TestName = "NotJson")]
    [TestCase("""{"status":"a","facts":[{"label":"1","value":"1"},{"label":"2","value":"1"},{"label":"3","value":"1"},{"label":"4","value":"1"}]}""", TestName = "FourFacts")]
    [TestCase("""{"status":"a","alerts":[{"severity":"fatal","text":"x"}]}""", TestName = "UnknownSeverity")]
    [TestCase("""{"status":"a\u0007b"}""", TestName = "ControlCharacter")]
    [TestCase(AppHomeCardLimits.TooLarge, TestName = "TooLargeMarker")]
    public void InvalidCardsAreRejected(string json) => Assert.That(AppHomeCardValidator.TryParse(json), Is.Null);

    [Test]
    public void LengthLimitsAreEnforced()
    {
        Assert.That(AppHomeCardValidator.TryParse(JsonSerializer.Serialize(new { status = new string('a', 81) })), Is.Null);
        Assert.That(AppHomeCardValidator.TryParse(JsonSerializer.Serialize(new { status = new string('a', 80) })), Is.Not.Null);
        Assert.That(AppHomeCardValidator.TryParse(JsonSerializer.Serialize(new { status = "a", alerts = Enumerable.Range(0, 6).Select(_ => new { severity = "info", text = "x" }) })), Is.Null);
    }

    [Test]
    public async Task HomeCardRunStoresTheCardAndALaterOneReplacesIt()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var app = Guid.NewGuid();
        await RunAsync(db, app, "home-card", "Succeeded", Valid, DateTime.UtcNow.AddMinutes(-10));
        await RunAsync(db, app, "home-card", "Succeeded", """{"status":"Newer"}""");

        var row = await db.AppHomeCards.SingleAsync();
        Assert.That(AppHomeCardValidator.Deserialize(row.CardJson)!.Status, Is.EqualTo("Newer"));
        Assert.That(row.LastFailedAtUtc, Is.Null);
    }

    [Test]
    public async Task AnyActionCanUpdateTheCardThroughHomeCardInItsResult()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var app = Guid.NewGuid();
        await RunAsync(db, app, "backup", "Succeeded", null);
        Assert.That(await db.AppHomeCards.CountAsync(), Is.Zero, "a result without homeCard changes nothing");

        await RunAsync(db, app, "backup", "Succeeded", Valid);
        Assert.That(AppHomeCardValidator.Deserialize((await db.AppHomeCards.SingleAsync()).CardJson)!.Facts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AFailedOrInvalidRunKeepsThePreviousCardAndMarksItLate()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var app = Guid.NewGuid();
        await RunAsync(db, app, "home-card", "Succeeded", Valid, DateTime.UtcNow.AddMinutes(-30));
        await RunAsync(db, app, "home-card", "Failed", null, DateTime.UtcNow.AddMinutes(-20));
        var failed = await db.AppHomeCards.AsNoTracking().SingleAsync();
        Assert.That(AppHomeCardValidator.Deserialize(failed.CardJson)!.Status, Is.EqualTo("All good"));
        Assert.That(failed.LastFailedAtUtc, Is.GreaterThan(failed.UpdatedAtUtc));

        await RunAsync(db, app, "home-card", "Succeeded", """{"status":"x","alerts":[{"severity":"nope","text":"y"}]}""", DateTime.UtcNow.AddMinutes(-10));
        Assert.That(AppHomeCardValidator.Deserialize((await db.AppHomeCards.AsNoTracking().SingleAsync()).CardJson)!.Status, Is.EqualTo("All good"));

        await RunAsync(db, app, "home-card", "Succeeded", """{"status":"Recovered"}""");
        var recovered = await db.AppHomeCards.AsNoTracking().SingleAsync();
        Assert.That(AppHomeCardValidator.Deserialize(recovered.CardJson)!.Status, Is.EqualTo("Recovered"));
        Assert.That(recovered.LastFailedAtUtc, Is.LessThan(recovered.UpdatedAtUtc), "no longer late");
    }

    [Test]
    public async Task AFailedRunOfAnotherActionDoesNotTouchTheCard()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var app = Guid.NewGuid();
        await RunAsync(db, app, "home-card", "Succeeded", Valid);
        await RunAsync(db, app, "backup", "Failed", null);
        Assert.That((await db.AppHomeCards.AsNoTracking().SingleAsync()).LastFailedAtUtc, Is.Null);
    }

    [Test]
    public async Task ScheduleIsCreatedStaggeredUpdatedAndRemoved()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid app = Guid.NewGuid(), owner = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, app, owner, "app");
        var service = new HomeCardScheduleService(db);

        await service.SyncAsync(app, owner, true, 900, default);
        var schedule = await db.ActionSchedules.SingleAsync();
        Assert.That(schedule.ActionKey, Is.EqualTo("home-card"));
        Assert.That(schedule.IntervalSeconds, Is.EqualTo(900));
        Assert.That(schedule.OwnerUserId, Is.EqualTo(owner));
        Assert.That(HomeCardSchedules.IsPlatformSchedule(schedule));
        Assert.That(schedule.NextRunAtUtc, Is.InRange(DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(901)));

        await service.SyncAsync(app, owner, true, 1800, default);
        Assert.That((await db.ActionSchedules.SingleAsync()).IntervalSeconds, Is.EqualTo(1800));

        await service.SyncAsync(app, owner, false, 900, default);
        Assert.That(await db.ActionSchedules.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ScheduleOfAPausedAppIsOffUntilResumed()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid app = Guid.NewGuid(), owner = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, app, owner, "app");
        (await db.WebApps.SingleAsync()).IsPaused = true;
        await db.SaveChangesAsync();

        await new HomeCardScheduleService(db).SyncAsync(app, owner, true, 900, default);

        var schedule = await db.ActionSchedules.SingleAsync();
        Assert.That(schedule.Enabled, Is.False);
        Assert.That(schedule.PausedByApp, Is.True);
    }

    [Test]
    public void IntervalComesFromTheContractAndIsBounded()
    {
        Assert.That(HomeCardSchedules.IntervalFor(new ApplicationContract()), Is.EqualTo(900));
        Assert.That(HomeCardSchedules.IntervalFor(new ApplicationContract { HomeCard = new() { IntervalSeconds = 60 } }), Is.EqualTo(300));
        Assert.That(HomeCardSchedules.IntervalFor(new ApplicationContract { HomeCard = new() { IntervalSeconds = 1200 } }), Is.EqualTo(1200));
    }

    [Test]
    public void HomeCardRunsGetLighterLimitsThanOtherActions()
    {
        var options = new MachineContainerResourceOptions();
        var normal = options.ToActionContainerPolicy();
        var card = options.ToHomeCardContainerPolicy();

        Assert.That(options.IsValid());
        Assert.That(card.Limits, Is.EqualTo(new HostContainerResourceLimits(0.25m, 128, 64)));
        Assert.That(normal.Limits, Is.EqualTo(new HostContainerResourceLimits(1.0m, 1024, 256)));
        Assert.That(card.RuntimeImage, Is.EqualTo(normal.RuntimeImage));
        Assert.That(options.HomeCardTimeoutSeconds, Is.EqualTo(30));
        Assert.That(new MachineContainerResourceOptions { HomeCardMemoryLimitMb = 8 }.IsValid(), Is.False);
    }

    [Test]
    public async Task HomeShowsTheStoredCardOfPinnedAppsAndGreysALateOne()
    {
        Guid owner = Guid.NewGuid(), fresh = Guid.NewGuid(), late = Guid.NewGuid(), plain = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        foreach (var id in new[] { fresh, late, plain }) AppAuthorizationTestData.SeedApp(db, id, owner, id.ToString("N"));
        await RunAsync(db, fresh, "home-card", "Succeeded", Valid);
        await RunAsync(db, late, "home-card", "Succeeded", Valid, DateTime.UtcNow.AddMinutes(-30));
        await RunAsync(db, late, "home-card", "Failed", null, DateTime.UtcNow.AddMinutes(-15));
        var authorization = new Services.ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(owner));
        var home = new HomeService(db, authorization, null!);
        foreach (var id in new[] { fresh, late, plain }) await home.PinAsync(owner, id, default);

        var pinned = (await home.GetAsync(owner, default)).Pinned.ToDictionary(item => item.App.Id);

        Assert.That(pinned[fresh].Card!.Card.Status, Is.EqualTo("All good"));
        Assert.That(pinned[fresh].Card!.Stale, Is.False);
        Assert.That(pinned[late].Card!.Stale, Is.True, "its last run failed");
        Assert.That(pinned[plain].Card, Is.Null);
    }

    [Test]
    public async Task PlatformScheduleNeedsNoPlanAndIsHiddenFromTheOwnersScheduleList()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid app = Guid.NewGuid(), owner = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, app, owner, "app");
        await new HomeCardScheduleService(db).SyncAsync(app, owner, true, 900, default);
        var schedule = await db.ActionSchedules.SingleAsync();
        var service = new Services.ActionScheduleService(db, null!, null!);

        Assert.DoesNotThrowAsync(() => service.ValidateForDispatchAsync(schedule, default), "no plan check, no manifest check");
        Assert.That(await service.ListAsync(owner, administrator: false, default), Is.Empty);
        Assert.That(await service.ListAsync(owner, administrator: true, default), Has.Count.EqualTo(1));
        Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetEnabledAsync(schedule.Id, false, owner, administrator: true, default), "nobody edits it by hand");
    }

    [Test]
    public async Task SyncTurnsAPausedScheduleBackOnOnceTheAppIsResumed()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        Guid app = Guid.NewGuid(), owner = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, app, owner, "app");
        var service = new HomeCardScheduleService(db);
        await service.SyncAsync(app, owner, true, 900, default);
        var schedule = await db.ActionSchedules.SingleAsync();
        schedule.Enabled = false; schedule.PausedByApp = true; schedule.NextRunAtUtc = null;
        await db.SaveChangesAsync();

        await service.SyncAsync(app, owner, true, 900, default);

        schedule = await db.ActionSchedules.AsNoTracking().SingleAsync();
        Assert.That(schedule.Enabled);
        Assert.That(schedule.PausedByApp, Is.False);
        Assert.That(schedule.NextRunAtUtc, Is.Not.Null);
    }
}
