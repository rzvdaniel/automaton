using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AdminSubscriptionPlanAssignmentAuditTests
{
    [Test]
    public async Task AssignPlan_WritesAuditEventWithTargetUserId()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var plan = CreateDisabledHostedRunnerPlan("Hosted Disabled");
        dbContext.Users.Add(CreateUser(targetUserId, "target@automaton.test"));
        dbContext.SubscriptionPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        using var requestCancellation = new CancellationTokenSource();
        var transactionExecutor = new AdminPlanInlineTransactionalEventExecutor(requestCancellation.Cancel);
        var controller = AdminSubscriptionPlansTestServiceFactory.CreateController(dbContext, actorUserId, transactionExecutor);
        var result = await controller.AssignPlan(targetUserId, new AssignPlanRequest { PlanId = plan.Id }, requestCancellation.Token);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        Assert.That(requestCancellation.IsCancellationRequested, Is.True);
        Assert.That(transactionExecutor.LastToken.CanBeCanceled, Is.False,
            "Plan assignment work must complete even when the request is canceled.");
        await AssertPlanAssignmentAuditEventAsync(dbContext, actorUserId, targetUserId, plan.Name);
    }

    private static async Task AssertPlanAssignmentAuditEventAsync(
        ApplicationDbContext dbContext,
        Guid actorUserId,
        Guid targetUserId,
        string planName)
    {
        var auditEvent = await dbContext.AuditEvents
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync(x => x.EventType == AuditEventTypes.SubscriptionPlanAssignedToUser);

        Assert.Multiple(() =>
        {
            Assert.That(auditEvent.Action, Is.EqualTo(AuditActions.Assigned));
            Assert.That(auditEvent.EntityType, Is.EqualTo(AuditEntityTypes.User));
            Assert.That(auditEvent.EntityId, Is.EqualTo(targetUserId));
            Assert.That(auditEvent.UserId, Is.EqualTo(actorUserId));
            Assert.That(auditEvent.TargetUserId, Is.EqualTo(targetUserId));
            Assert.That(auditEvent.Message, Does.Contain(planName));
            Assert.That(auditEvent.MetadataJson, Does.Contain(planName));
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ApplicationUser CreateUser(Guid userId, string email) => new()
    {
        Id = userId,
        Email = email,
        UserName = email,
        Name = "Target User",
        PublicUsername = $"user-{userId.ToString("N")[..8]}",
        SecurityStamp = Guid.NewGuid().ToString("N")
    };

    private static SubscriptionPlan CreateDisabledHostedRunnerPlan(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsActive = true,
        CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(PlanCapabilities.Default)
    };
}
