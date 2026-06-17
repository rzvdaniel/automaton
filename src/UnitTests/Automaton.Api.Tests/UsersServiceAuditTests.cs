using System.Security.Claims;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public class UsersServiceAuditTests
{
    [Test]
    public async Task SetDisabledAsync_WritesAuditEventWithTargetUserId()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var targetUser = CreateUser(targetUserId, "target@automaton.test");

        dbContext.Users.Add(targetUser);
        await dbContext.SaveChangesAsync();

        var httpContextAccessor = CreateHttpContextAccessor(actorUserId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(dbContext, userContextService, httpContextAccessor);
        var service = new UsersService(
            new TestUserManager(targetUser),
            new TestRoleManager(),
            dbContext,
            userContextService,
            auditEventService);

        await service.SetDisabledAsync(targetUserId, disabled: true, CancellationToken.None);

        var auditEvent = await dbContext.AuditEvents.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(auditEvent.EventType, Is.EqualTo(AuditEventTypes.UserDisabled));
            Assert.That(auditEvent.Action, Is.EqualTo(AuditActions.Disabled));
            Assert.That(auditEvent.UserId, Is.EqualTo(actorUserId));
            Assert.That(auditEvent.TargetUserId, Is.EqualTo(targetUserId));
            Assert.That(auditEvent.Message, Does.Contain("disabled"));
        });
    }

    [Test]
    public async Task AddRole_WritesAuditEventWithTargetUserId()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var targetUser = CreateUser(targetUserId, "target@automaton.test");

        dbContext.Users.Add(targetUser);
        await dbContext.SaveChangesAsync();

        var httpContextAccessor = CreateHttpContextAccessor(actorUserId, "admin@automaton.test");
        var userContextService = new UserContextService(httpContextAccessor);
        IAuditEventService auditEventService = new AuditEventService(dbContext, userContextService, httpContextAccessor);
        var service = new UsersService(
            new TestUserManager(targetUser),
            new TestRoleManager(),
            dbContext,
            userContextService,
            auditEventService);

        await service.AddRole(targetUserId, UserRole.Administrator);

        var auditEvent = await dbContext.AuditEvents.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(auditEvent.EventType, Is.EqualTo(AuditEventTypes.UserRoleChanged));
            Assert.That(auditEvent.Action, Is.EqualTo(AuditActions.Added));
            Assert.That(auditEvent.UserId, Is.EqualTo(actorUserId));
            Assert.That(auditEvent.TargetUserId, Is.EqualTo(targetUserId));
            Assert.That(auditEvent.MetadataJson, Does.Contain(UserRole.Administrator));
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ApplicationUser CreateUser(Guid userId, string email)
    {
        return new ApplicationUser
        {
            Id = userId,
            Email = email,
            UserName = email,
            Name = "Target User",
            PublicUsername = "target-user",
            SecurityStamp = Guid.NewGuid().ToString("N")
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

    private sealed class TestUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser user;

        public TestUserManager(ApplicationUser user)
            : base(
                new StubUserStore(user),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            this.user = user;
        }

        public override Task<ApplicationUser?> FindByIdAsync(string userId)
        {
            return Task.FromResult<ApplicationUser?>(user.Id.ToString() == userId ? user : null);
        }

        public override Task<IdentityResult> SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd)
        {
            user.LockoutEnd = lockoutEnd;
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role)
        {
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class TestRoleManager : RoleManager<ApplicationRole>
    {
        public TestRoleManager()
            : base(
                new StubRoleStore(),
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                NullLogger<RoleManager<ApplicationRole>>.Instance)
        {
        }
    }

    private sealed class StubRoleStore : IRoleStore<ApplicationRole>
    {
        public void Dispose()
        {
        }

        public Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<string?> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult<string?>(role.Id.ToString());
        public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);
        public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken cancellationToken)
        {
            role.Name = roleName;
            return Task.CompletedTask;
        }
        public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken) => Task.FromResult(role.NormalizedName);
        public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken cancellationToken)
        {
            role.NormalizedName = normalizedName;
            return Task.CompletedTask;
        }
        public Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) => Task.FromResult<ApplicationRole?>(null);
        public Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken) => Task.FromResult<ApplicationRole?>(null);
    }

    private sealed class StubUserStore : IUserEmailStore<ApplicationUser>, IUserLockoutStore<ApplicationUser>
    {
        private readonly ApplicationUser user;

        public StubUserStore(ApplicationUser user)
        {
            this.user = user;
        }

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
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(user.Id.ToString() == userId ? user : null);
        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);
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
        public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);
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
