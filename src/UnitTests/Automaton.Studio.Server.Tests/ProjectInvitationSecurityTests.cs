using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class ProjectInvitationSecurityTests
{
    [Test]
    public async Task Complete_WhenExistingInvitedUserAlreadyHasPassword_ReturnsBadRequest()
    {
        await using var dbContext = CreateDbContext();
        var projectId = Guid.NewGuid();
        const string token = "project-invite-token";

        dbContext.Projects.Add(new Project
        {
            Id = projectId,
            Name = "project",
            DisplayName = "Project",
            Description = "Test project",
            OwnerId = Guid.NewGuid(),
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            IsActive = true
        });

        dbContext.Set<InvitationToken>().Add(new InvitationToken
        {
            Id = Guid.NewGuid(),
            Token = token,
            ProjectId = projectId,
            Email = "invited@example.com",
            Role = "Viewer",
            InvitedBy = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsConsumed = false
        });

        await dbContext.SaveChangesAsync();

        var invitedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "invited@example.com",
            UserName = "invited@example.com",
            Name = "Invited",
            PublicUsername = "invited",
            PasswordHash = "existing-hash",
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        var controller = new InvitationsController(
            new InvitationTokenService(dbContext),
            dbContext,
            new TestUserManager(invitedUser),
            null!,
            null!,
            null!);

        var result = await controller.Complete(token, new InvitationsController.AcceptInvitationRequest
        {
            Password = "NewPassword1!"
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await dbContext.ProjectMembers.AnyAsync(), Is.False);

        var invitation = await dbContext.Set<InvitationToken>().SingleAsync(i => i.Token == token);
        Assert.That(invitation.IsConsumed, Is.False);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
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

        public override Task<ApplicationUser?> FindByEmailAsync(string email)
        {
            return Task.FromResult<ApplicationUser?>(
                string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase) ? user : null);
        }

        public override Task<bool> HasPasswordAsync(ApplicationUser user)
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(user.PasswordHash));
        }
    }

    private sealed class StubUserStore : IUserEmailStore<ApplicationUser>, IUserPasswordStore<ApplicationUser>
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

        public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

        public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(!string.IsNullOrWhiteSpace(user.PasswordHash));
    }
}
