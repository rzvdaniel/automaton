using System.Security.Claims;
using System.Data.Common;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        var existingDefault = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Existing default",
            IsDefault = true
        };
        dbContext.SubscriptionPlans.Add(existingDefault);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, Guid.NewGuid());

        Assert.CatchAsync<OperationCanceledException>(async () => await controller.CreatePlan(new CreateSubscriptionPlanRequest
        {
            Name = "Replacement default",
            IsActive = true,
            IsDefault = true
        }, cancellationSource.Token));

        dbContext.ChangeTracker.Clear();
        Assert.That(await dbContext.SubscriptionPlans.CountAsync(plan => plan.IsDefault), Is.EqualTo(1));
        Assert.That(await dbContext.SubscriptionPlans.AnyAsync(plan => plan.Name == "Existing default" && plan.IsDefault), Is.True);
        Assert.That(await dbContext.SubscriptionPlans.AnyAsync(plan => plan.Name == "Replacement default"), Is.False);
    }

    [Test]
    public async Task UpdatePlan_CancellationAfterClearingDefault_RollsBackBothChanges()
    {
        using var cancellationSource = new CancellationTokenSource();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateRelationalDbContextAsync(connection, cancellationSource);
        var existingDefault = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Existing default",
            IsDefault = true
        };
        var replacement = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Replacement",
            IsDefault = false
        };
        dbContext.SubscriptionPlans.AddRange(existingDefault, replacement);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, Guid.NewGuid());

        Assert.CatchAsync<OperationCanceledException>(async () => await controller.UpdatePlan(
            replacement.Id,
            new UpdateSubscriptionPlanRequest { IsDefault = true },
            cancellationSource.Token));

        dbContext.ChangeTracker.Clear();
        Assert.That(await dbContext.SubscriptionPlans.CountAsync(plan => plan.IsDefault), Is.EqualTo(1));
        Assert.That(await dbContext.SubscriptionPlans.AnyAsync(plan => plan.Name == "Existing default" && plan.IsDefault), Is.True);
        Assert.That(await dbContext.SubscriptionPlans.AnyAsync(plan => plan.Name == "Replacement" && !plan.IsDefault), Is.True);
    }

    [Test]
    public async Task CreatePlan_WritesAuditEvent()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var controller = CreateController(dbContext, actorUserId);

        var actionResult = await controller.CreatePlan(new CreateSubscriptionPlanRequest
        {
            Name = "Pro",
            Description = "Paid plan",
            PriceUsd = 19,
            IsActive = true,
            IsDefault = false,
            StripePriceId = "price_123"
        }, CancellationToken.None);

        Assert.That(actionResult.Result, Is.TypeOf<CreatedAtActionResult>());

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

    [Test]
    public async Task AssignPlan_WritesAuditEventWithTargetUserId()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var user = CreateUser(targetUserId, "target@automaton.test");
        var plan = CreateDisabledHostedRunnerPlan("Hosted Disabled");

        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        using var requestCancellation = new CancellationTokenSource();
        var transactionExecutor = new InlineTransactionalEventExecutor(requestCancellation.Cancel);
        var controller = CreateController(dbContext, actorUserId, transactionExecutor);

        var result = await controller.AssignPlan(targetUserId, new AssignPlanRequest
        {
            PlanId = plan.Id
        }, requestCancellation.Token);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        Assert.That(requestCancellation.IsCancellationRequested, Is.True);
        Assert.That(transactionExecutor.LastToken.CanBeCanceled, Is.False,
            "Plan assignment work must complete even when the request is canceled.");

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
            Assert.That(auditEvent.Message, Does.Contain(plan.Name));
            Assert.That(auditEvent.MetadataJson, Does.Contain(plan.Name));
        });
    }

    [Test]
    public async Task SetHostedRunnerTestAccess_WritesAuditEventWithTargetUserId()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var user = CreateUser(targetUserId, "target@automaton.test");

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, actorUserId);

        var result = await controller.SetHostedRunnerTestAccess(targetUserId, new SetHostedRunnerTestAccessRequest
        {
            Enabled = true
        });

        Assert.That(result, Is.TypeOf<NoContentResult>());

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

    private static AdminSubscriptionPlansController CreateController(
        ApplicationDbContext dbContext,
        Guid actorUserId,
        ITransactionalEventExecutor? transactionalEventExecutor = null)
    {
        var httpContextAccessor = CreateHttpContextAccessor(actorUserId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(dbContext, userContextService, httpContextAccessor);
        var apiKeyService = new ApiKeyService(
            dbContext,
            NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            auditEventService);
        var hostedRunnerProvisioningService = new HostedRunnerProvisioningService(
            dbContext,
            TestRunnerEnrollmentFactory.Create(dbContext, auditEventService),
            new NoOpEventPublisher(),
            new TestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions
            {
                Enabled = false
            }),
            new SubscriptionResourcePolicyResolver(new HostedRunnerSpecificationResolver(dbContext)),
            new AccountStorageProjectAllocationService(dbContext),
            auditEventService,
            NullLogger<HostedRunnerProvisioningService>.Instance);

        return new AdminSubscriptionPlansController(
            dbContext,
            new TestUserManager(),
            hostedRunnerProvisioningService,
            new HostedRunnerSpecificationResolver(dbContext),
            auditEventService,
            transactionalEventExecutor ?? new InlineTransactionalEventExecutor());
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
        CancellationTokenSource cancellationSource)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new CancelAfterDefaultPlanUpdateInterceptor(cancellationSource))
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

    private sealed class CancelAfterDefaultPlanUpdateInterceptor(CancellationTokenSource cancellationSource)
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
            if (command.CommandText.Contains("UPDATE \"SubscriptionPlans\"", StringComparison.OrdinalIgnoreCase))
                cancellationSource.Cancel();
        }
    }

    private static ApplicationUser CreateUser(Guid userId, string email)
    {
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

    private static SubscriptionPlan CreateDisabledHostedRunnerPlan(string name)
    {
        return new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
            {
                HostedRunner = new HostedRunnerCapability
                {
                    Enabled = false
                }
            })
        };
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid userId, string userName)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName)
        ], "Test"));

        return new HttpContextAccessor
        {
            HttpContext = httpContext
        };
    }

    private sealed class FakeHostedRunnerRemoteCommandExecutor
    {
        public bool CanHandle(RunnerHost host, HostedRunnerOptions options) => true;

        public Task<HostedRunnerRemoteExecutionResult> ExecuteScriptAsync(
            RunnerHost host,
            string script,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HostedRunnerRemoteExecutionResult(true, 0, string.Empty, string.Empty));
        }

        public Task<HostedRunnerRemoteExecutionResult> ExecuteProvisionScriptAsync(
            RunnerHost host,
            string script,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HostedRunnerRemoteExecutionResult(true, 0, string.Empty, string.Empty));
        }
    }

    private sealed class InlineTransactionalEventExecutor : ITransactionalEventExecutor
    {
        private readonly Action? onExecute;

        public InlineTransactionalEventExecutor(Action? onExecute = null)
        {
            this.onExecute = onExecute;
        }

        public CancellationToken LastToken { get; private set; }

        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            onExecute?.Invoke();
            return operation(cancellationToken);
        }
    }

    private sealed class TestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;

        public T Get(string? name) => value;
    }

    private sealed class TestUserManager()
        : UserManager<ApplicationUser>(
            new StubUserStore(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance)
    {
    }

    private sealed class StubUserStore : IUserEmailStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString());
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
        {
            user.Email = email;
            return Task.CompletedTask;
        }
        public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Email);
        public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.EmailConfirmed);
        public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
        {
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }
        public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) => Task.FromResult<ApplicationUser?>(null);
        public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);
        public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
        {
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }
        public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);
        public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }
        public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
