using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppPauseTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AppId = Guid.NewGuid();

    private sealed class Harness : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        public RecordingQueue Queue { get; } = new();
        public Guid RunnerId { get; } = Guid.NewGuid();

        public Harness()
        {
            var plan = new SubscriptionPlan
            {
                Id = Guid.NewGuid(), Name = "Test",
                CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities())
            };
            Db.AddRange(plan, new ApplicationUser
            {
                Id = OwnerId, UserName = "owner", PublicUsername = "owner", SubscriptionPlanId = plan.Id, SubscriptionPlan = plan
            });
            Db.SaveChanges();
            AppAuthorizationTestData.SeedApp(Db, AppId, OwnerId, "my-app");
        }

        public AppPauseService ServiceFor(Guid userId)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
            };
            var context = new UserContextService(new HttpContextAccessor { HttpContext = http });
            return new AppPauseService(
                Db, new ResourceAuthorizationService(Db, context),
                new ActionScheduleService(Db, new AllowAllResolver(), new StubAudit()),
                Queue, context, new StubAudit(), NullLogger<AppPauseService>.Instance);
        }

        public ActionSchedule AddSchedule(string name, bool enabled)
        {
            var schedule = new ActionSchedule
            {
                Id = Guid.NewGuid(), OwnerType = ActionScheduleOwnerTypes.User, OwnerUserId = OwnerId, ApplicationId = AppId,
                ActionKey = "backup", Name = name, ScheduleType = ActionScheduleTypes.Interval, IntervalSeconds = 300,
                TimeZone = "Etc/UTC", ExecutionScope = ActionExecutionScopes.AppRunner,
                ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.SkipIfRunning, MisfirePolicy = ActionScheduleMisfirePolicies.Skip,
                InputJson = "{}", Enabled = enabled, NextRunAtUtc = enabled ? DateTime.UtcNow.AddMinutes(5) : null
            };
            Db.ActionSchedules.Add(schedule);
            Db.SaveChanges();
            return schedule;
        }

        public AppDeploymentTarget AddRunningTarget()
        {
            // Saving an app creates its primary (Live) target; give that one a Runner and a running runtime.
            Db.Runners.Add(new Runner { Id = RunnerId, Name = "runner", Purpose = RunnerPurposes.Managed, ConnectionId = "connection", Status = "online" });
            var target = Db.AppDeploymentTargets.Single(item => item.WebAppId == AppId && item.IsDefault);
            target.ExecutionRunnerId = RunnerId;
            target.GenericRuntimeDeployed = true;
            target.Status = DeploymentTargetStatus.Running;
            Db.SaveChanges();
            return target;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Test]
    public async Task Pause_StopsRuntime_TurnsOffOnlyEnabledSchedules_AndResumeRestoresExactlyThose()
    {
        await using var h = new Harness();
        var target = h.AddRunningTarget();
        var on = h.AddSchedule("nightly", enabled: true);
        var off = h.AddSchedule("manual", enabled: false);
        var service = h.ServiceFor(OwnerId);

        var paused = await service.PauseAsync(AppId, administrator: false, default);

        Assert.That(paused.Paused, Is.True);
        Assert.That(paused.SchedulesChanged, Is.EqualTo(1));
        Assert.That((await h.Db.WebApps.SingleAsync()).IsPaused, Is.True);
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.Enabled, Is.False);
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.NextRunAtUtc, Is.Null);
        Assert.That(h.Queue.Sent.Select(item => item.Command.Kind), Is.EqualTo(new[] { AppPauseService.PauseCommandKind }));
        Assert.That(h.Queue.Sent.Single().RunnerId, Is.EqualTo(h.RunnerId));
        Assert.That(h.Queue.Sent.Single().Command.Read<PauseAppRuntimeRequest>().DeploymentTargetId, Is.EqualTo(target.Id));

        var resumed = await service.ResumeAsync(AppId, administrator: false, default);

        Assert.That(resumed.Paused, Is.False);
        Assert.That(resumed.SchedulesChanged, Is.EqualTo(1));
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.Enabled, Is.True);
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.NextRunAtUtc, Is.Not.Null);
        Assert.That((await h.Db.ActionSchedules.FindAsync(off.Id))!.Enabled, Is.False);
        Assert.That(h.Queue.Sent.Last().Command.Kind, Is.EqualTo(AppPauseService.ResumeCommandKind));
    }

    [Test]
    public async Task Pause_KeepsDataSettingsAndSchedulesRows()
    {
        await using var h = new Harness();
        h.AddSchedule("nightly", enabled: true);

        await h.ServiceFor(OwnerId).PauseAsync(AppId, administrator: false, default);

        var app = await h.Db.WebApps.SingleAsync();
        Assert.That(app.PublicUrl, Is.EqualTo("my-app"));
        Assert.That(app.ApplicationContractJson, Is.Not.Empty);
        Assert.That(await h.Db.ActionSchedules.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task PausingTwice_IsHarmless_AndDoesNotForgetWhichSchedulesWereOn()
    {
        await using var h = new Harness();
        var on = h.AddSchedule("nightly", enabled: true);
        var service = h.ServiceFor(OwnerId);

        await service.PauseAsync(AppId, administrator: false, default);
        var second = await service.PauseAsync(AppId, administrator: false, default);
        await service.ResumeAsync(AppId, administrator: false, default);

        Assert.That(second.SchedulesChanged, Is.Zero);
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.Enabled, Is.True);
    }

    [Test]
    public async Task ASchedule_CreatedOrSavedOnWhilePaused_IsRefused_AndOneTurnedOffByHandStaysOffOnResume()
    {
        await using var h = new Harness();
        var on = h.AddSchedule("nightly", enabled: true);
        var service = h.ServiceFor(OwnerId);
        var schedules = new ActionScheduleService(h.Db, new AllowAllResolver(), new StubAudit());
        await service.PauseAsync(AppId, administrator: false, default);

        var request = new SaveActionScheduleRequest
        {
            ApplicationId = AppId, ActionKey = "backup", Name = "nightly", ScheduleType = ActionScheduleTypes.Interval,
            IntervalSeconds = 300, TimeZone = "Etc/UTC", ExecutionScope = ActionExecutionScopes.AppRunner,
            ConcurrencyPolicy = ActionScheduleConcurrencyPolicies.SkipIfRunning, MisfirePolicy = ActionScheduleMisfirePolicies.Skip,
            Enabled = true
        };
        Assert.That(async () => await schedules.CreateAsync(request, OwnerId, false, default), Throws.InvalidOperationException);
        Assert.That(async () => await schedules.UpdateAsync(on.Id, request, OwnerId, false, default), Throws.InvalidOperationException);

        // Saved off by hand while paused: resuming must respect that.
        request.Enabled = false;
        await schedules.UpdateAsync(on.Id, request, OwnerId, false, default);
        await service.ResumeAsync(AppId, administrator: false, default);
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.Enabled, Is.False);
    }

    [Test]
    public async Task ResumingAgain_RetriesAScheduleThatCouldNotBeTurnedOn()
    {
        await using var h = new Harness();
        var on = h.AddSchedule("nightly", enabled: true);
        await h.ServiceFor(OwnerId).PauseAsync(AppId, administrator: false, default);
        var plan = await h.Db.SubscriptionPlans.SingleAsync();
        plan.CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
        {
            ScheduledActions = new ScheduledActionsCapability { Enabled = false }
        });
        await h.Db.SaveChangesAsync();

        var first = await h.ServiceFor(OwnerId).ResumeAsync(AppId, administrator: false, default);
        Assert.That(first.Warnings, Has.Count.EqualTo(1));
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.PausedByApp, Is.True);

        plan.CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities());
        await h.Db.SaveChangesAsync();
        var second = await h.ServiceFor(OwnerId).ResumeAsync(AppId, administrator: false, default);

        Assert.That(second.SchedulesChanged, Is.EqualTo(1));
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.Enabled, Is.True);
    }

    [Test]
    public async Task ASchedule_CannotBeTurnedOnWhilePaused()
    {
        await using var h = new Harness();
        var schedule = h.AddSchedule("manual", enabled: false);
        await h.ServiceFor(OwnerId).PauseAsync(AppId, administrator: false, default);
        var schedules = new ActionScheduleService(h.Db, new AllowAllResolver(), new StubAudit());

        Assert.That(async () => await schedules.SetEnabledAsync(schedule.Id, true, OwnerId, false, default),
            Throws.InvalidOperationException.With.Message.Contains("paused"));
    }

    [Test]
    public async Task SomeoneWhoCannotSeeTheApp_GetsNotFound_AndNothingChanges()
    {
        await using var h = new Harness();
        var on = h.AddSchedule("nightly", enabled: true);

        Assert.That(async () => await h.ServiceFor(Guid.NewGuid()).PauseAsync(AppId, administrator: false, default),
            Throws.TypeOf<KeyNotFoundException>());

        Assert.That((await h.Db.WebApps.SingleAsync()).IsPaused, Is.False);
        Assert.That((await h.Db.ActionSchedules.FindAsync(on.Id))!.Enabled, Is.True);
        Assert.That(h.Queue.Sent, Is.Empty);
    }

    [Test]
    public async Task AProjectViewerCannotPause_ButADeveloperCan()
    {
        await using var h = new Harness();
        var projectId = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var developer = Guid.NewGuid();
        h.Db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, OwnerId, "team"));
        h.Db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = AppId });
        h.Db.ProjectMembers.AddRange(
            new ProjectMember { ProjectId = projectId, UserId = viewer, Role = ProjectRole.Viewer, IsActive = true },
            new ProjectMember { ProjectId = projectId, UserId = developer, Role = ProjectRole.Developer, IsActive = true });
        await h.Db.SaveChangesAsync();

        Assert.That(async () => await h.ServiceFor(viewer).PauseAsync(AppId, administrator: false, default),
            Throws.TypeOf<UnauthorizedAccessException>());
        Assert.That((await h.ServiceFor(developer).PauseAsync(AppId, administrator: false, default)).Paused, Is.True);
    }

    [Test]
    public async Task AnAdministrator_CanPauseAnyApp()
    {
        await using var h = new Harness();

        var state = await h.ServiceFor(Guid.NewGuid()).PauseAsync(AppId, administrator: true, default);

        Assert.That(state.Paused, Is.True);
    }

    [Test]
    public async Task AnUnknownApp_IsNotFound()
    {
        await using var h = new Harness();

        Assert.That(async () => await h.ServiceFor(OwnerId).PauseAsync(Guid.NewGuid(), administrator: false, default),
            Throws.TypeOf<KeyNotFoundException>());
    }

    [Test]
    public async Task ARunnerThatCannotBeReached_StillLeavesTheAppPaused_WithAWarning()
    {
        await using var h = new Harness();
        h.AddRunningTarget();
        h.Queue.Fail = true;

        var state = await h.ServiceFor(OwnerId).PauseAsync(AppId, administrator: false, default);

        Assert.That(state.Paused, Is.True);
        Assert.That(state.Warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ADeployWhilePaused_StopsTheNewRuntimeAgain_AndSaysSo()
    {
        await using var h = new Harness();
        var target = h.AddRunningTarget();
        var service = h.ServiceFor(OwnerId);

        Assert.That(await service.KeepPausedAfterDeployAsync(AppId, target.Id, h.RunnerId, OwnerId, default), Is.Null);
        await service.PauseAsync(AppId, administrator: false, default);
        h.Queue.Sent.Clear();

        var note = await service.KeepPausedAfterDeployAsync(AppId, target.Id, h.RunnerId, OwnerId, default);

        Assert.That(note, Does.Contain("paused"));
        Assert.That(h.Queue.Sent.Single().Command.Kind, Is.EqualTo(AppPauseService.PauseCommandKind));
    }

    [Test]
    public void ThePausedPage_Answers503WithAShortHtmlMessage()
    {
        var page = RuntimeTunnelService.PausedPage();

        Assert.That(page.StatusCode, Is.EqualTo(503));
        Assert.That(page.HeaderValues["Content-Type"], Is.EqualTo(new[] { "text/html; charset=utf-8" }));
        Assert.That(System.Text.Encoding.UTF8.GetString(page.BodyBytes!), Does.Contain("This app is paused"));
    }

    private sealed class RecordingQueue : IAppRuntimeCommandQueue
    {
        public List<(Guid RunnerId, AppActionCommand Command)> Sent { get; } = [];
        public bool Fail { get; set; }

        public Task QueueAsync(Guid runnerId, Guid actorUserId, AppActionCommand command, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Runner unavailable");
            Sent.Add((runnerId, command));
            return Task.CompletedTask;
        }
    }

    private sealed class StubAudit : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AllowAllResolver : IActionScheduleTargetResolver
    {
        public Task<AppActionManifest> ResolveAsync(Guid appId, string actionKey, Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(new AppActionManifest
            {
                Spec = new AppActionSpec
                {
                    Schedulable = true, AllowedExecutionScopes = [ActionExecutionScopes.AppRunner], MinimumIntervalSeconds = 60
                }
            });
    }
}
