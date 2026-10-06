using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ActionScheduleEnableValidationTests
{
    [Test]
    public async Task Enable_RejectsWhenPlanCapabilityWasRemoved()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Plan.CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
        {
            ScheduledActions = new ScheduledActionsCapability { Enabled = false }
        });
        await fixture.Db.SaveChangesAsync();
        Assert.That(async () => await fixture.EnableAsync(), Throws.InvalidOperationException);
    }

    [Test]
    public async Task Enable_RejectsWhenAppPermissionWasRemoved()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Resolver.Error = new UnauthorizedAccessException("permission removed");
        Assert.That(async () => await fixture.EnableAsync(), Throws.TypeOf<UnauthorizedAccessException>());
    }

    [Test]
    public async Task Enable_RejectsWhenActionWasDeleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Resolver.Error = new KeyNotFoundException("Action not found");
        Assert.That(async () => await fixture.EnableAsync(), Throws.TypeOf<KeyNotFoundException>());
    }

    [Test]
    public async Task Enable_RejectsWhenManifestIsNoLongerSchedulable()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Resolver.Manifest.Spec.Schedulable = false;
        Assert.That(async () => await fixture.EnableAsync(), Throws.InvalidOperationException);
    }

    [Test]
    public async Task Enable_RejectsWhenActionMinimumWasIncreased()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Resolver.Manifest.Spec.MinimumIntervalSeconds = 600;
        Assert.That(async () => await fixture.EnableAsync(), Throws.InvalidOperationException);
    }

    [Test]
    public async Task Enable_RejectsExpiredOneTimeSchedule()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Schedule.ScheduleType = ActionScheduleTypes.OneTime;
        fixture.Schedule.IntervalSeconds = null;
        fixture.Schedule.RunAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        Assert.That(async () => await fixture.EnableAsync(), Throws.InvalidOperationException);
    }

    [Test]
    public async Task Enable_RejectsCronWithoutFutureOccurrence()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Schedule.ScheduleType = ActionScheduleTypes.Cron;
        fixture.Schedule.IntervalSeconds = null;
        fixture.Schedule.CronExpression = "0 0 30 2 *";
        await fixture.Db.SaveChangesAsync();
        Assert.That(async () => await fixture.EnableAsync(), Throws.InvalidOperationException);
    }

    [TestCase(1)]
    [TestCase(60)]
    [TestCase(86400)]
    public async Task Dispatch_AcceptsDueOrDelayedOneTimeSchedule(int delaySeconds)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Schedule.ScheduleType = ActionScheduleTypes.OneTime;
        fixture.Schedule.IntervalSeconds = null;
        fixture.Schedule.RunAtUtc = DateTime.UtcNow.AddSeconds(-delaySeconds);
        // One-time schedules are disabled by the Worker after publishing their occurrence.
        fixture.Schedule.Enabled = false;
        await fixture.Service.ValidateForDispatchAsync(fixture.Schedule, CancellationToken.None);
        Assert.That(fixture.Resolver.Calls, Is.EqualTo(1));
        await Assert.ThatAsync(async () => await fixture.EnableAsync(), Throws.InvalidOperationException);
    }

    [Test]
    public async Task Dispatch_RejectsOneTimeScheduleWithoutRunTime()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Schedule.ScheduleType = ActionScheduleTypes.OneTime;
        fixture.Schedule.RunAtUtc = null;
        await Assert.ThatAsync(async () => await fixture.Service.ValidateForDispatchAsync(fixture.Schedule, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.EqualTo("One-time run time is required."));
    }

    [Test]
    public async Task Dispatch_StillRejectsRemovedPermission()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Schedule.ScheduleType = ActionScheduleTypes.OneTime;
        fixture.Schedule.RunAtUtc = DateTime.UtcNow.AddMinutes(-1);
        fixture.Resolver.Error = new UnauthorizedAccessException("permission removed");
        await Assert.ThatAsync(async () => await fixture.Service.ValidateForDispatchAsync(fixture.Schedule, CancellationToken.None),
            Throws.TypeOf<UnauthorizedAccessException>());
    }

    [Test]
    public async Task Dispatch_StillRejectsRemovedPlanCapability()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Schedule.ScheduleType = ActionScheduleTypes.OneTime;
        fixture.Schedule.RunAtUtc = DateTime.UtcNow.AddMinutes(-1);
        fixture.Plan.CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
        {
            ScheduledActions = new ScheduledActionsCapability { Enabled = false }
        });
        await fixture.Db.SaveChangesAsync();
        await Assert.ThatAsync(async () => await fixture.Service.ValidateForDispatchAsync(fixture.Schedule, CancellationToken.None),
            Throws.InvalidOperationException);
    }

    [Test]
    public async Task Dispatch_StillRejectsActionNoLongerSchedulable()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Schedule.ScheduleType = ActionScheduleTypes.OneTime;
        fixture.Schedule.RunAtUtc = DateTime.UtcNow.AddMinutes(-1);
        fixture.Resolver.Manifest.Spec.Schedulable = false;
        await Assert.ThatAsync(async () => await fixture.Service.ValidateForDispatchAsync(fixture.Schedule, CancellationToken.None),
            Throws.InvalidOperationException.With.Message.EqualTo("The Action manifest does not allow scheduling."));
    }

    [Test]
    public async Task Enable_RevalidatesAndPersistsFutureOccurrence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var enabled = await fixture.EnableAsync();
        Assert.Multiple(() =>
        {
            Assert.That(enabled.Enabled, Is.True);
            Assert.That(enabled.NextRunAtUtc, Is.GreaterThan(DateTime.UtcNow));
            Assert.That(fixture.Resolver.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Model_RepresentsSystemOwnershipWithoutAdministratorForeignKey()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entity = fixture.Db.Model.FindEntityType(typeof(ActionSchedule))!;
        Assert.That(entity.FindProperty(nameof(ActionSchedule.OwnerUserId))!.IsNullable, Is.True);

        var systemSchedule = new ActionSchedule
        {
            Id = Guid.NewGuid(),
            OwnerType = ActionScheduleOwnerTypes.System,
            OwnerUserId = null,
            ApplicationId = InfrastructureAppTrust.PlatformAppId,
            ActionKey = "platform-action",
            Name = "Platform",
            InputJson = "{}"
        };
        fixture.Db.ActionSchedules.Add(systemSchedule);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Users.Remove(fixture.User);
        await fixture.Db.SaveChangesAsync();
        Assert.That(await fixture.Db.ActionSchedules.AnyAsync(x => x.Id == systemSchedule.Id), Is.True);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ApplicationDbContext Db { get; init; }
        public required ApplicationUser User { get; init; }
        public required SubscriptionPlan Plan { get; init; }
        public required ActionSchedule Schedule { get; init; }
        public required StubResolver Resolver { get; init; }
        public required ActionScheduleService Service { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new ApplicationDbContext(options);
            var plan = new SubscriptionPlan
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities())
            };
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "schedule-user",
                PublicUsername = "schedule-user",
                SubscriptionPlanId = plan.Id,
                SubscriptionPlan = plan
            };
            var schedule = new ActionSchedule
            {
                Id = Guid.NewGuid(),
                OwnerType = ActionScheduleOwnerTypes.User,
                OwnerUserId = user.Id,
                ApplicationId = Guid.NewGuid(),
                ActionKey = "scheduled-action",
                Name = "Schedule",
                ScheduleType = ActionScheduleTypes.Interval,
                IntervalSeconds = 300,
                TimeZone = "Etc/UTC",
                ExecutionScope = ActionExecutionScopes.AppRunner,
                ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.SkipIfRunning,
                MisfirePolicy = ActionScheduleMisfirePolicies.Skip,
                InputJson = "{}",
                Enabled = false
            };
            db.AddRange(plan, user, schedule);
            await db.SaveChangesAsync();
            var resolver = new StubResolver();
            return new Fixture
            {
                Db = db,
                Plan = plan,
                User = user,
                Schedule = schedule,
                Resolver = resolver,
                Service = new ActionScheduleService(db, resolver, new StubTrustedValidator(), new StubAudit())
            };
        }

        public Task<ActionSchedule> EnableAsync()
            => Service.SetEnabledAsync(Schedule.Id, true, User.Id, administrator: false, CancellationToken.None);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class StubResolver : IActionScheduleTargetResolver
    {
        public Exception? Error { get; set; }
        public int Calls { get; private set; }
        public AppActionManifest Manifest { get; } = new()
        {
            Spec = new AppActionSpec
            {
                Schedulable = true,
                AllowedExecutionScopes = [ActionExecutionScopes.AppRunner],
                MinimumIntervalSeconds = 300
            }
        };

        public Task<AppActionManifest> ResolveAsync(Guid appId, string actionKey, Guid userId, bool system, CancellationToken cancellationToken)
        {
            Calls++;
            return Error == null ? Task.FromResult(Manifest) : Task.FromException<AppActionManifest>(Error);
        }
    }

    private sealed class StubTrustedValidator : ITrustedSystemActionScheduleDispatchValidator
    {
        public Task<AppActionManifest> ValidateAsync(ActionSchedule schedule, CancellationToken cancellationToken)
            => throw new InvalidOperationException("System validation is not expected in these user-schedule tests.");
    }

    private sealed class StubAudit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
