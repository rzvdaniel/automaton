using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AdminHostedRunnerTestAccessAuditTests
{
    [Test]
    public async Task SetHostedRunnerTestAccess_WritesAuditEventWithTargetUserId()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        dbContext.Users.Add(CreateUser(targetUserId));
        await dbContext.SaveChangesAsync();

        var result = await AdminSubscriptionPlansTestServiceFactory.CreateController(dbContext, actorUserId)
            .SetHostedRunnerTestAccess(targetUserId, new SetHostedRunnerTestAccessRequest
            {
                Enabled = true
            });

        Assert.That(result, Is.TypeOf<NoContentResult>());
        await AssertAccessAuditAsync(dbContext, actorUserId, targetUserId);
    }

    private static async Task AssertAccessAuditAsync(
        ApplicationDbContext dbContext,
        Guid actorUserId,
        Guid targetUserId)
    {
        var auditEvent = await dbContext.AuditEvents.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(auditEvent.EventType, Is.EqualTo(AuditEventTypes.HostedRunnerTestPlanAccessChanged));
            Assert.That(auditEvent.Action, Is.EqualTo(AuditActions.Changed));
            Assert.That(auditEvent.EntityType, Is.EqualTo(AuditEntityTypes.User));
            Assert.That(auditEvent.EntityId, Is.EqualTo(targetUserId));
            Assert.That(auditEvent.UserId, Is.EqualTo(actorUserId));
            Assert.That(auditEvent.TargetUserId, Is.EqualTo(targetUserId));
            Assert.That(auditEvent.MetadataJson, Does.Contain("true").IgnoreCase);
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ApplicationUser CreateUser(Guid userId)
    {
        var email = "target@automaton.test";
        return new ApplicationUser
        {
            Id = userId,
            Email = email,
            UserName = email,
            Name = "Target User",
            PublicUsername = $"user-{userId.ToString("N")[..8]}",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }

}
