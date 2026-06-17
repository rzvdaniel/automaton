using Automaton.Api.Constants;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class AdminLogsControllerTests
{
    [Test]
    public async Task GetAuditEvents_AppliesFiltersAndReturnsPagedResult()
    {
        await using var dbContext = CreateDbContext();
        var targetUserId = Guid.NewGuid();
        var appEntityId = Guid.NewGuid();
        var runnerEntityId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        dbContext.AuditEvents.AddRange(
            new AuditEvent
            {
                Id = Guid.NewGuid(),
                Category = "Audit",
                Action = AuditActions.Created,
                EntityType = AuditEntityTypes.ApiKey,
                EntityId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                UserName = "other@automaton.test",
                Outcome = AuditOutcomes.Success,
                EventType = AuditEventTypes.ApiKeyCreated,
                Message = "Other event",
                MetadataJson = "{\"name\":\"Other\"}",
                CreatedAtUtc = now.AddMinutes(-20)
            },
            new AuditEvent
            {
                Id = Guid.NewGuid(),
                Category = "Audit",
                Action = AuditActions.Published,
                EntityType = AuditEntityTypes.App,
                EntityId = appEntityId,
                UserId = Guid.NewGuid(),
                TargetUserId = targetUserId,
                UserName = "admin@automaton.test",
                Outcome = AuditOutcomes.Success,
                EventType = AuditEventTypes.AppPublished,
                Message = "App was published for release",
                MetadataJson = "{\"name\":\"Space Invaders\"}",
                CreatedAtUtc = now.AddMinutes(-10)
            },
            new AuditEvent
            {
                Id = Guid.NewGuid(),
                Category = "Audit",
                Action = AuditActions.Published,
                EntityType = AuditEntityTypes.App,
                EntityId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                TargetUserId = targetUserId,
                UserName = "admin@automaton.test",
                Outcome = AuditOutcomes.Failed,
                EventType = AuditEventTypes.AppPublished,
                Message = "App publish failed",
                MetadataJson = "{\"name\":\"Broken App\"}",
                CreatedAtUtc = now.AddMinutes(-5)
            },
            new AuditEvent
            {
                Id = Guid.NewGuid(),
                Category = "Audit",
                Action = AuditActions.Assigned,
                EntityType = AuditEntityTypes.Runner,
                EntityId = runnerEntityId,
                UserId = Guid.NewGuid(),
                TargetUserId = targetUserId,
                UserName = "admin@automaton.test",
                Outcome = AuditOutcomes.Success,
                EventType = AuditEventTypes.RunnerAssignedToApp,
                Message = "Runner assigned",
                MetadataJson = "{\"runnerName\":\"Local\"}",
                CreatedAtUtc = now.AddMinutes(-2)
            });

        await dbContext.SaveChangesAsync();

        var controller = new AdminLogsController(dbContext);

        var actionResult = await controller.GetAuditEvents(
            new AuditEventQuery
            {
                EventType = AuditEventTypes.AppPublished,
                EntityType = AuditEntityTypes.App,
                TargetUserId = targetUserId,
                Outcome = AuditOutcomes.Success,
                Search = "release",
                FromUtc = now.AddMinutes(-15),
                ToUtc = now.AddMinutes(-1),
                Page = 1,
                PageSize = 1
            },
            CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var page = okResult!.Value as PagedResult<AuditEventDto>;
        Assert.That(page, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(page!.TotalCount, Is.EqualTo(1));
            Assert.That(page.Page, Is.EqualTo(1));
            Assert.That(page.PageSize, Is.EqualTo(1));
            Assert.That(page.Items, Has.Count.EqualTo(1));
            Assert.That(page.Items[0].EntityId, Is.EqualTo(appEntityId));
            Assert.That(page.Items[0].EntityDisplay, Is.EqualTo("Space Invaders"));
            Assert.That(page.Items[0].TargetUserId, Is.EqualTo(targetUserId));
            Assert.That(page.Items[0].Outcome, Is.EqualTo(AuditOutcomes.Success));
        });
    }

    [Test]
    public async Task GetAuditEvents_NormalizesInvalidPagingValues()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTime.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            dbContext.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                Category = "Audit",
                Action = AuditActions.Created,
                EntityType = AuditEntityTypes.App,
                EntityId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                UserName = $"user{i}@automaton.test",
                Outcome = AuditOutcomes.Success,
                EventType = AuditEventTypes.AppCreated,
                Message = $"App {i} created",
                MetadataJson = $"{{\"name\":\"App {i}\"}}",
                CreatedAtUtc = now.AddMinutes(-i)
            });
        }

        await dbContext.SaveChangesAsync();

        var controller = new AdminLogsController(dbContext);

        var actionResult = await controller.GetAuditEvents(
            new AuditEventQuery
            {
                Page = 0,
                PageSize = 500
            },
            CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        var page = okResult!.Value as PagedResult<AuditEventDto>;

        Assert.That(page, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(page!.Page, Is.EqualTo(1));
            Assert.That(page.PageSize, Is.EqualTo(200));
            Assert.That(page.TotalCount, Is.EqualTo(3));
            Assert.That(page.Items, Has.Count.EqualTo(3));
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
