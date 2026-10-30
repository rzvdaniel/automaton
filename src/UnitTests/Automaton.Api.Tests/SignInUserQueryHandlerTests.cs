using Automaton.Api.Entities;
using Automaton.Api.Application.Queries.Handlers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Automaton.Api.Application.Queries;

namespace Automaton.Api.Tests;

[TestFixture]
public class SignInUserQueryHandlerTests
{
    [Test]
    public void Handle_WhenUserIsMissing_ThrowsInvalidCredentials()
    {
        var userManager = new TestUserManager();
        var signInManager = new TestSignInManager(userManager)
        {
            NextResult = SignInResult.Failed
        };
        var handler = new SignInUserQueryHandler(userManager, signInManager, null!, null!);

        var act = async () => await handler.Handle(new SignInUserQuery(4, "missing@example.com", "pw"), CancellationToken.None);

        Assert.That(act, Throws.Exception.With.Message.EqualTo("Invalid credentials."));
    }

    [Test]
    public async Task Handle_WhenLockoutIsDisabled_EnablesItBeforePasswordCheck()
    {
        var user = CreateUser(lockoutEnabled: false);
        var userManager = new TestUserManager(user);
        var signInManager = new TestSignInManager(userManager)
        {
            NextResult = SignInResult.Failed
        };
        var handler = new SignInUserQueryHandler(userManager, signInManager, null!, null!);

        try
        {
            await handler.Handle(new SignInUserQuery(4, user.Email!, "wrong-password"), CancellationToken.None);
        }
        catch
        {
        }

        Assert.That(userManager.SetLockoutEnabledCalls, Is.EqualTo(1));
        Assert.That(user.LockoutEnabled, Is.True);
        Assert.That(signInManager.LockoutOnFailureArgs, Is.Not.Empty);
        Assert.That(signInManager.LockoutOnFailureArgs.Single(), Is.True);
    }

    [Test]
    public void Handle_WhenUserIsLockedOut_ThrowsInvalidCredentials()
    {
        var user = CreateUser(lockoutEnabled: true);
        var userManager = new TestUserManager(user);
        var signInManager = new TestSignInManager(userManager)
        {
            NextResult = SignInResult.LockedOut
        };
        var handler = new SignInUserQueryHandler(userManager, signInManager, null!, null!);

        var act = async () => await handler.Handle(new SignInUserQuery(4, user.Email!, "pw"), CancellationToken.None);

        Assert.That(act, Throws.Exception.With.Message.EqualTo("Invalid credentials."));
    }

    private static ApplicationUser CreateUser(bool lockoutEnabled)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            UserName = "user@example.com",
            Name = "User",
            PublicUsername = "user",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = lockoutEnabled
        };
    }

    private sealed class TestUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser? user;

        public TestUserManager(ApplicationUser? user = null)
            : base(
                new StubUserStore(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new EmptyServiceProvider(),
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            this.user = user;
        }

        public int SetLockoutEnabledCalls { get; private set; }

        public override Task<ApplicationUser?> FindByEmailAsync(string email)
        {
            var matches = user != null && string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(matches ? user : null);
        }

        public override Task<ApplicationUser?> FindByNameAsync(string userName)
        {
            var matches = user != null && string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(matches ? user : null);
        }

        public override Task<IdentityResult> SetLockoutEnabledAsync(ApplicationUser user, bool enabled)
        {
            SetLockoutEnabledCalls++;
            user.LockoutEnabled = enabled;
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class TestSignInManager : SignInManager<ApplicationUser>
    {
        public TestSignInManager(UserManager<ApplicationUser> userManager)
            : base(
                userManager,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                new TestUserClaimsPrincipalFactory(userManager),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
                new DefaultUserConfirmation<ApplicationUser>())
        {
        }

        public SignInResult NextResult { get; set; } = SignInResult.Success;

        public List<bool> LockoutOnFailureArgs { get; } = [];

        public override Task<SignInResult> CheckPasswordSignInAsync(ApplicationUser user, string password, bool lockoutOnFailure)
        {
            LockoutOnFailureArgs.Add(lockoutOnFailure);
            return Task.FromResult(NextResult);
        }
    }

    private sealed class TestUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser>
    {
        public TestUserClaimsPrincipalFactory(UserManager<ApplicationUser> userManager)
            : base(userManager, Microsoft.Extensions.Options.Options.Create(new IdentityOptions()))
        {
        }
    }

    private sealed class StubUserStore :
        IUserEmailStore<ApplicationUser>,
        IUserPasswordStore<ApplicationUser>,
        IUserLockoutStore<ApplicationUser>
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

        public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

        public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(!string.IsNullOrWhiteSpace(user.PasswordHash));

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnd);

        public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount++;
            return Task.FromResult(user.AccessFailedCount);
        }

        public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.AccessFailedCount);

        public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.LockoutEnabled);

        public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
        {
            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }
    }
}
