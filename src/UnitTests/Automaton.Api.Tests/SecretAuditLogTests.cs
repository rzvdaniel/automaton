using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

/// <summary>Secret changes and deliveries are audited with names, scopes and versions, never values.</summary>
public sealed class SecretAuditLogTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Value1 = "first-value-4f1c";
    private const string Value2 = "second-value-9a7e";

    [Test]
    public async Task ProjectSecretChangesAndDeliveriesAreRecordedWithoutValues()
    {
        var (db, service, developer, appId) = Create();
        var runnerId = Guid.NewGuid();
        db.Runners.Add(new() { Id = runnerId, Name = "runner", RunnerUsers = [] });
        await db.SaveChangesAsync();
        var target = await db.AppDeploymentTargets.SingleAsync(item => item.WebAppId == appId);
        target.ExecutionRunnerId = runnerId;
        await db.SaveChangesAsync();
        var targetId = target.Id;

        await service.SetAsync(appId, developer, AppSecretScopes.Project, "STRIPE_KEY", Value1, null, None);
        await service.SetAsync(appId, developer, AppSecretScopes.Project, "STRIPE_KEY", Value2, null, None);
        await service.ResolveForExecutionAsync(appId, new AppSecretService.DeliveryActor(developer, null), None);
        await service.ResolveForRuntimeAsync(appId, targetId, runnerId, None);
        await service.DeleteAsync(appId, developer, AppSecretScopes.Project, "STRIPE_KEY", None);

        var project = await service.HistoryAsync(appId, developer, AppSecretScopes.Project, None);
        Assert.That(project.Select(entry => (entry.EventType, entry.Name, entry.Version, entry.AppId, entry.UserId)), Is.EqualTo(new[]
        {
            (AuditEventTypes.SecretDeleted, (string?)"STRIPE_KEY", (int?)null, (Guid?)appId, (Guid?)developer),
            (AuditEventTypes.SecretSet, "STRIPE_KEY", 2, appId, developer),
            (AuditEventTypes.SecretSet, "STRIPE_KEY", 1, appId, developer),
        }));

        var deliveries = await service.HistoryAsync(appId, developer, AppSecretScopes.App, None);
        Assert.Multiple(() =>
        {
            Assert.That(deliveries, Has.Count.EqualTo(2));
            Assert.That(deliveries.All(entry => entry.EventType == AuditEventTypes.SecretsDelivered && entry.AppId == appId));
            Assert.That(deliveries.Select(entry => entry.RunnerId), Is.EqualTo(new Guid?[] { runnerId, null }), "newest first: runtime start, then Action run");
            Assert.That(deliveries[1].UserId, Is.EqualTo(developer));
            Assert.That(deliveries.SelectMany(entry => entry.Delivered),
                Is.All.EqualTo(new SecretDelivery("STRIPE_KEY", AppSecretScopes.Project, 2)));
        });
        var stored = await db.AuditEvents.Select(entry => entry.Message + entry.MetadataJson).ToListAsync();
        Assert.That(stored, Has.None.Contains(Value1).And.None.Contains(Value2));
    }

    [Test]
    public async Task DispatchResolutionDoesNotSavePendingChangesUntilRecorded()
    {
        var (db, service, developer, appId) = Create();
        await service.SetAsync(appId, developer, AppSecretScopes.Project, "STRIPE_KEY", Value1, null, None);
        var pending = new AuditEvent
        {
            Id = Guid.NewGuid(), Category = "Audit", Action = "Created", EntityType = AuditEntityTypes.ApiKey, EventType = "ApiKeyCreated",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.AuditEvents.Add(pending);

        var resolved = await service.ResolveForDispatchAsync(appId, None);

        Assert.That(resolved.Values["STRIPE_KEY"], Is.EqualTo(Value1));
        Assert.That(db.Entry(pending).State, Is.EqualTo(EntityState.Added), "resolving must not save the caller's pending changes");
        await service.RecordDeliveryAsync(appId, resolved, new AppSecretService.DeliveryActor(developer, null), None);
        Assert.That(await service.HistoryAsync(appId, developer, AppSecretScopes.App, None), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReadingHistoryNeedsAccessToTheVault()
    {
        var (_, service, developer, appId) = Create();
        await service.SetAsync(appId, developer, AppSecretScopes.Project, "STRIPE_KEY", Value1, null, None);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.HistoryAsync(appId, Guid.NewGuid(), AppSecretScopes.Project, None));
        Assert.That(await service.HistoryAsync(appId, developer, AppSecretScopes.User, None), Is.Empty,
            "a user's own vault history does not show other vaults");
    }

    [Test]
    public async Task EntriesPastRetentionAreRemoved()
    {
        var (db, service, developer, appId) = Create();
        await service.SetAsync(appId, developer, AppSecretScopes.Project, "STRIPE_KEY", Value1, null, None);
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), Category = "Audit", Action = "Created", EntityType = AuditEntityTypes.ApiKey, EventType = "ApiKeyCreated",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-400)
        });
        await db.SaveChangesAsync();

        var removed = await SecretAuditLog.RemoveExpiredAsync(db, DateTime.UtcNow.AddDays(SecretAuditLog.RetentionDays + 1), None);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(await db.AuditEvents.Select(entry => entry.EntityType).SingleAsync(), Is.EqualTo(AuditEntityTypes.ApiKey),
            "retention applies only to secret audit entries");
    }

    private static (ApplicationDbContext Db, AppSecretService Service, Guid Developer, Guid AppId) Create()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var projectId = Guid.NewGuid();
        var developer = Guid.NewGuid();
        var appId = Guid.NewGuid();
        db.Projects.Add(AppAuthorizationTestData.CreateProject(projectId, Guid.NewGuid(), "project"));
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId, UserId = developer, Role = ProjectRole.Developer, JoinedAt = DateTime.UtcNow, IsActive = true
        });
        AppAuthorizationTestData.SeedApp(db, appId, developer, "audited-app");
        db.WebApps.Single(app => app.Id == appId).ApplicationContractJson = ApplicationContractSerialization.Serialize(
            new ApplicationContract { Secrets = [new() { Name = "STRIPE_KEY" }] });
        db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = appId, AddedAt = DateTime.UtcNow });
        db.SaveChanges();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var users = AppAuthorizationTestData.CreateUserContextService(developer);
        var audit = new SecretAuditLog(new AuditEventService(db, users, new HttpContextAccessor()), db);
        return (db, new AppSecretService(db, new SecretStore(db, protector), protector, new ResourceAuthorizationService(db, users), audit),
            developer, appId);
    }
}
