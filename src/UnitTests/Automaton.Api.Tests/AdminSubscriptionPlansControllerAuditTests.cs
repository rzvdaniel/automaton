using System.Security.Claims;
using System.Data.Common;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Messaging;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using DotNetCore.CAP.Transport;

namespace Automaton.Api.Tests;

[TestFixture]
public class AdminSubscriptionPlansControllerAuditTests
{
    [Test]
    public async Task CreatePlan_CancellationAfterClearingDefault_RollsBackBothChanges()
    {
        using var cancellationSource = new CancellationTokenSource();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateRelationalDbContextAsync(connection, cancellationSource);
        await SeedPlanAsync(dbContext, "Existing default", isDefault: true);

        var controller = AdminSubscriptionPlansTestServiceFactory.CreateController(dbContext, Guid.NewGuid());

        Assert.CatchAsync<OperationCanceledException>(async () => await controller.CreatePlan(new CreateSubscriptionPlanRequest
        {
            Name = "Replacement default",
            IsActive = true,
            IsDefault = true
        }, cancellationSource.Token));

        await AssertDefaultPlanRollbackAsync(dbContext, "Existing default", "Replacement default");
    }

    [Test]
    public async Task CreatePlan_CancellationDuringAudit_RollsBackPlanAndDefaultChange()
    {
        using var cancellationSource = new CancellationTokenSource();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateRelationalDbContextAsync(connection, cancellationSource, cancelAfterDefaultUpdate: false);
        await SeedPlanAsync(dbContext, "Existing default", isDefault: true);

        var actorUserId = Guid.NewGuid();
        var controller = AdminSubscriptionPlansTestServiceFactory.CreateController(
            dbContext,
            actorUserId,
            auditEventService: new CancelingAuditEventService(
                AdminSubscriptionPlansTestServiceFactory.CreateAuditEventService(dbContext, actorUserId),
                cancellationSource));

        Assert.CatchAsync<OperationCanceledException>(async () => await controller.CreatePlan(new CreateSubscriptionPlanRequest
        {
            Name = "Replacement default",
            IsActive = true,
            IsDefault = true
        }, cancellationSource.Token));

        await AssertDefaultPlanRollbackAsync(dbContext, "Existing default", "Replacement default");
    }

    [Test]
    public async Task UpdatePlan_CancellationAfterClearingDefault_RollsBackBothChanges()
    {
        using var cancellationSource = new CancellationTokenSource();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateRelationalDbContextAsync(connection, cancellationSource);
        await SeedPlanAsync(dbContext, "Existing default", isDefault: true);
        var replacementId = await SeedPlanAsync(dbContext, "Replacement", isDefault: false);

        var controller = AdminSubscriptionPlansTestServiceFactory.CreateController(dbContext, Guid.NewGuid());

        Assert.CatchAsync<OperationCanceledException>(async () => await controller.UpdatePlan(
            replacementId,
            new UpdateSubscriptionPlanRequest { IsDefault = true },
            cancellationSource.Token));

        await AssertDefaultPlanRollbackAsync(dbContext, "Existing default", "Replacement", replacementExists: true);
    }

    [Test]
    public async Task CreatePlan_WritesAuditEvent()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var controller = AdminSubscriptionPlansTestServiceFactory.CreateController(dbContext, actorUserId);

        var actionResult = await controller.CreatePlan(CreatePaidPlanRequest(), CancellationToken.None);

        Assert.That(actionResult.Result, Is.TypeOf<CreatedAtActionResult>());
        await AssertPlanCreatedAuditEventAsync(dbContext, actorUserId);
    }

    private static async Task AssertPlanCreatedAuditEventAsync(ApplicationDbContext dbContext, Guid actorUserId)
    {
        var auditEvent = await dbContext.AuditEvents.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(auditEvent.EventType, Is.EqualTo(AuditEventTypes.SubscriptionPlanCreated));
            Assert.That(auditEvent.Action, Is.EqualTo(AuditActions.Created));
            Assert.That(auditEvent.EntityType, Is.EqualTo(AuditEntityTypes.SubscriptionPlan));
            Assert.That(auditEvent.UserId, Is.EqualTo(actorUserId));
            Assert.That(auditEvent.TargetUserId, Is.Null);
            Assert.That(auditEvent.Message, Does.Contain("Pro"));
            Assert.That(auditEvent.MetadataJson, Does.Contain("hasStripePriceId"));
        });
    }

    private static CreateSubscriptionPlanRequest CreatePaidPlanRequest() => new()
    {
        Name = "Pro",
        Description = "Paid plan",
        PriceUsd = 19,
        IsActive = true,
        IsDefault = false,
        StripePriceId = "price_123"
    };

    private static async Task<Guid> SeedPlanAsync(
        ApplicationDbContext dbContext,
        string name,
        bool isDefault)
    {
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = name, IsDefault = isDefault };
        dbContext.SubscriptionPlans.Add(plan);
        await dbContext.SaveChangesAsync();
        return plan.Id;
    }

    private static async Task AssertDefaultPlanRollbackAsync(
        ApplicationDbContext dbContext,
        string existingPlanName,
        string replacementPlanName,
        bool replacementExists = false)
    {
        dbContext.ChangeTracker.Clear();
        Assert.That(await dbContext.SubscriptionPlans.CountAsync(plan => plan.IsDefault), Is.EqualTo(1));
        Assert.That(await dbContext.SubscriptionPlans.AnyAsync(plan => plan.Name == existingPlanName && plan.IsDefault), Is.True);
        Assert.That(await dbContext.SubscriptionPlans.AnyAsync(plan => plan.Name == replacementPlanName && !plan.IsDefault), Is.EqualTo(replacementExists));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<ApplicationDbContext> CreateRelationalDbContextAsync(
        SqliteConnection connection,
        CancellationTokenSource cancellationSource,
        bool cancelAfterDefaultUpdate = true)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new CancelAfterDefaultPlanUpdateInterceptor(cancellationSource, cancelAfterDefaultUpdate))
            .Options;
        var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "SubscriptionPlans" (
                "Id" TEXT NOT NULL PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Description" TEXT NULL,
                "PriceUsd" TEXT NOT NULL,
                "StripeProductId" TEXT NULL,
                "StripePriceId" TEXT NULL,
                "IsDefault" INTEGER NOT NULL DEFAULT 0,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CapabilitiesJson" TEXT NOT NULL,
                "SortOrder" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NULL
            );
            """);
        return dbContext;
    }

    private sealed class CancelAfterDefaultPlanUpdateInterceptor(
        CancellationTokenSource cancellationSource,
        bool cancelAfterDefaultUpdate)
        : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            CancelIfPlanDefaultsUpdated(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            CancelIfPlanDefaultsUpdated(command);
            return ValueTask.FromResult(result);
        }

        private void CancelIfPlanDefaultsUpdated(DbCommand command)
        {
            if (cancelAfterDefaultUpdate
                && command.CommandText.Contains("UPDATE \"SubscriptionPlans\"", StringComparison.OrdinalIgnoreCase))
                cancellationSource.Cancel();
        }
    }

    private sealed class CancelingAuditEventService(
        IAuditEventService inner,
        CancellationTokenSource cancellationSource) : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
        {
            cancellationSource.Cancel();
            return inner.WriteAsync(request, cancellationToken);
        }

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
        {
            cancellationSource.Cancel();
            return inner.WriteAsync(action, entityType, entityId, message, metadata, outcome, failOnError, cancellationToken);
        }
    }

}
