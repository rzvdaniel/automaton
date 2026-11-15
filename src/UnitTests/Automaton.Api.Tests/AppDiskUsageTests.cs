using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.ServerHealth;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDiskUsageTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid AppId = Guid.NewGuid();

    private static (ApplicationDbContext Db, Func<Guid, AppDiskUsageService> For) Create()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(), Name = "Test", CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities())
        };
        db.AddRange(plan, new ApplicationUser { Id = OwnerId, UserName = "owner", PublicUsername = "owner", SubscriptionPlanId = plan.Id, SubscriptionPlan = plan });
        db.SaveChanges();
        AppAuthorizationTestData.SeedApp(db, AppId, OwnerId, "my-app");
        return (db, userId =>
        {
            var context = AppAuthorizationTestData.CreateUserContextService(userId);
            return new AppDiskUsageService(db, new ResourceAuthorizationService(db, context), context);
        });
    }

    private static void Report(ApplicationDbContext db, string name, long images, DateTime at)
    {
        var host = new MachineHostHealth(at, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, AppDisk: [new AppDiskUsage(AppId, 10, 20, 30, 40, images)]);
        db.Runners.Add(new Runner
        {
            Id = Guid.NewGuid(), Name = name, Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            MachineHostHealthJson = JsonSerializer.Serialize(host, new JsonSerializerOptions(JsonSerializerDefaults.Web)), MachineHostHealthMeasuredAtUtc = at
        });
        db.SaveChanges();
    }

    [Test]
    public async Task OwnerAndAdministratorGetTheBreakdown_ThePartsAreSummed()
    {
        var (db, serviceFor) = Create();
        await using var _ = db;
        Report(db, "machine", 100, DateTime.UtcNow);

        var owner = await serviceFor(OwnerId).GetAsync(AppId, administrator: false, default);
        var admin = await serviceFor(Guid.NewGuid()).GetAsync(AppId, administrator: true, default);

        Assert.Multiple(() =>
        {
            Assert.That(owner!.TotalBytes, Is.EqualTo(200));
            Assert.That((owner.FilesBytes, owner.DataBytes, owner.LogsBytes, owner.ContainerBytes, owner.ImageBytes), Is.EqualTo((10L, 20L, 30L, 40L, 100L)));
            Assert.That(admin!.TotalBytes, Is.EqualTo(200));
        });
    }

    [Test]
    public async Task WithoutAMeasurement_TheAnswerIsNull_AndTheMostMeasuredReportWins()
    {
        var (db, serviceFor) = Create();
        await using var _ = db;
        Assert.That(await serviceFor(OwnerId).GetAsync(AppId, false, default), Is.Null);

        Report(db, "small", 1, DateTime.UtcNow.AddMinutes(-1));
        Report(db, "large", 900, DateTime.UtcNow.AddMinutes(-2));

        Assert.That((await serviceFor(OwnerId).GetAsync(AppId, false, default))!.ImageBytes, Is.EqualTo(900));
    }

    [Test]
    public async Task StrangersAndViewersAreRefused_AndUnknownOrDeletedAppsAreNotFound()
    {
        var (db, serviceFor) = Create();
        await using var _ = db;
        var projectId = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, OwnerId, "team"));
        db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = AppId });
        db.ProjectMembers.Add(new ProjectMember { ProjectId = projectId, UserId = viewer, Role = ProjectRole.Viewer, IsActive = true });
        await db.SaveChangesAsync();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<KeyNotFoundException>(() => serviceFor(Guid.NewGuid()).GetAsync(AppId, false, default));
            Assert.ThrowsAsync<UnauthorizedAccessException>(() => serviceFor(viewer).GetAsync(AppId, false, default));
            Assert.ThrowsAsync<KeyNotFoundException>(() => serviceFor(OwnerId).GetAsync(Guid.NewGuid(), false, default));
            Assert.ThrowsAsync<KeyNotFoundException>(() => serviceFor(OwnerId).GetAsync(Guid.NewGuid(), true, default));
        });
    }
}
