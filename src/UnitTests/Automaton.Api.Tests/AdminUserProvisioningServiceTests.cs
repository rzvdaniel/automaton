using System.Security.Claims;
using System.Text;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>Administrators add users by invitation (emailed one-time link) or by creating them with a password.</summary>
[TestFixture]
public sealed class AdminUserProvisioningServiceTests
{
    [Test]
    public async Task CreatedUserCanSignInWithTheGeneratedPasswordShownOnce()
    {
        var fixture = await Fixture.CreateAsync();

        var created = await fixture.Service.CreateAsync(new AdminCreateUserRequest("Tester@Example.com"), false, default);

        var user = await fixture.Users.FindByIdAsync(created.UserId.ToString());
        Assert.Multiple(async () =>
        {
            Assert.That(created.GeneratedPassword, Has.Length.EqualTo(20));
            Assert.That(await fixture.Users.CheckPasswordAsync(user!, created.GeneratedPassword!), Is.True);
            Assert.That(user!.EmailConfirmed, Is.True);
            Assert.That(user.RegistrationProvider, Is.EqualTo(RegistrationProviders.AdminCreated));
            Assert.That(await fixture.Users.IsInRoleAsync(user, UserRole.Administrator), Is.False);
            Assert.That(fixture.Email.Sent, Is.Empty, "creating a user sends no email");
            var audit = await fixture.Db.AuditEvents.SingleAsync();
            Assert.That(audit.EventType, Is.EqualTo(AuditEventTypes.UserCreatedByAdministrator));
            Assert.That(audit.MetadataJson, Does.Not.Contain(created.GeneratedPassword!));
        });
    }

    [Test]
    public async Task ChosenPasswordIsKeptAndNotReturned()
    {
        var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(new AdminCreateUserRequest("chosen@example.com", "Chosen", "Chosen-Pass-123"), false, default);
        var user = await fixture.Users.FindByIdAsync(created.UserId.ToString());
        Assert.That(created.GeneratedPassword, Is.Null);
        Assert.That(await fixture.Users.CheckPasswordAsync(user!, "Chosen-Pass-123"), Is.True);
        Assert.That(user!.Name, Is.EqualTo("Chosen"));
    }

    [Test]
    public async Task InvitedUserSetsThePasswordOnceFromTheEmailedLink()
    {
        var fixture = await Fixture.CreateAsync();

        var invited = await fixture.Service.InviteAsync(new AdminInviteUserRequest("person@example.com", "Person"), "Admin", false, default);

        var user = await fixture.Users.FindByIdAsync(invited.UserId.ToString());
        Assert.That(invited.InvitationSent, Is.True);
        Assert.That(await fixture.Users.HasPasswordAsync(user!), Is.False);
        Assert.That(user!.EmailConfirmed, Is.False);
        var email = fixture.Email.Sent.Single();
        Assert.That(email.Template, Is.EqualTo(AdminUserProvisioningService.InvitationTemplate));
        var link = new Uri(email.Variables["SetupPasswordUrl"]);
        Assert.That(link.AbsolutePath, Is.EqualTo("/reset-password"));
        var query = QueryHelpers.ParseQuery(link.Query);
        Assert.That((string?)query["create"], Is.EqualTo("true"));

        // The Studio page posts the code to the reset endpoint, which this mirrors.
        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["code"]!));
        await fixture.UsersService.ResetPasswordAsync(user.Id, token, "Fresh-Pass-123");

        user = await fixture.Users.FindByIdAsync(invited.UserId.ToString());
        Assert.That(await fixture.Users.CheckPasswordAsync(user!, "Fresh-Pass-123"), Is.True);
        Assert.That(user!.EmailConfirmed, Is.True, "setting the password from the emailed link proves the address");
        Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UsersService.ResetPasswordAsync(user.Id, token, "Again-Pass-123"),
            "the link works only once");
    }

    [Test]
    public async Task InvitingAPendingAccountResendsAndAnActiveOneIsAConflict()
    {
        var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.InviteAsync(new AdminInviteUserRequest("person@example.com"), "Admin", false, default);
        var again = await fixture.Service.InviteAsync(new AdminInviteUserRequest("person@example.com"), "Admin", false, default);
        Assert.That(again.UserId, Is.EqualTo(first.UserId));
        Assert.That(again.Resent, Is.True);
        Assert.That(fixture.Email.Sent, Has.Count.EqualTo(2));

        await fixture.Service.CreateAsync(new AdminCreateUserRequest("active@example.com"), false, default);
        Assert.ThrowsAsync<AdminUserConflictException>(() =>
            fixture.Service.InviteAsync(new AdminInviteUserRequest("active@example.com"), "Admin", false, default));
        Assert.ThrowsAsync<AdminUserConflictException>(() =>
            fixture.Service.CreateAsync(new AdminCreateUserRequest("person@example.com"), false, default));
    }

    [Test]
    public async Task OnlySystemAdministratorsCreateAdministrators()
    {
        var fixture = await Fixture.CreateAsync();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.CreateAsync(new AdminCreateUserRequest("admin2@example.com", Administrator: true), false, default));
        Assert.That(await fixture.Db.Users.CountAsync(), Is.Zero);

        var created = await fixture.Service.CreateAsync(new AdminCreateUserRequest("admin2@example.com", Administrator: true), true, default);
        var user = await fixture.Users.FindByIdAsync(created.UserId.ToString());
        Assert.That(await fixture.Users.IsInRoleAsync(user!, UserRole.Administrator), Is.True);
    }

    [Test]
    public async Task FailedEmailKeepsTheAccountAndSaysHowToResend()
    {
        var fixture = await Fixture.CreateAsync(emailWorks: false);
        var invited = await fixture.Service.InviteAsync(new AdminInviteUserRequest("person@example.com"), "Admin", false, default);
        Assert.That(invited.InvitationSent, Is.False);
        Assert.That(invited.Warning, Does.Contain("invite the same address again"));
        Assert.That(await fixture.Db.Users.CountAsync(), Is.EqualTo(1));
    }

    [TestCase("not-an-email")]
    [TestCase("")]
    [TestCase("Name <a@b.com>")]
    public async Task RejectsInvalidEmail(string address)
    {
        var fixture = await Fixture.CreateAsync();
        Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(new AdminCreateUserRequest(address), false, default));
    }

    [Test]
    public void GeneratedPasswordsUseEveryCharacterClass()
    {
        for (var i = 0; i < 50; i++)
        {
            var password = AdminUserProvisioningService.GeneratePassword();
            Assert.That(password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit)
                && password.Any(c => !char.IsLetterOrDigit(c)), Is.True, password.Length.ToString());
        }
    }

    private sealed class Fixture
    {
        public required ApplicationDbContext Db { get; init; }
        public required UserManager<ApplicationUser> Users { get; init; }
        public required UsersService UsersService { get; init; }
        public required AdminUserProvisioningService Service { get; init; }
        public required RecordingEmailService Email { get; init; }

        public static async Task<Fixture> CreateAsync(bool emailWorks = true)
        {
            var db = AppAuthorizationTestData.CreateDbContext();
            db.Roles.Add(new ApplicationRole { Id = Guid.NewGuid(), Name = UserRole.Administrator, NormalizedName = UserRole.Administrator.ToUpperInvariant() });
            await db.SaveChangesAsync();

            var identity = new IdentityOptions();
            identity.Password.RequiredLength = 8;
            var users = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid, IdentityUserClaim<Guid>, ApplicationUserRole,
                    IdentityUserLogin<Guid>, IdentityUserToken<Guid>, IdentityRoleClaim<Guid>>(db),
                Options.Create(identity), new PasswordHasher<ApplicationUser>(),
                [new UserValidator<ApplicationUser>()], [new PasswordValidator<ApplicationUser>()],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
                NullLogger<UserManager<ApplicationUser>>.Instance);
            users.RegisterTokenProvider(TokenOptions.DefaultProvider, new DataProtectorTokenProvider<ApplicationUser>(
                new EphemeralDataProtectionProvider(), Options.Create(new DataProtectionTokenProviderOptions()),
                NullLogger<DataProtectorTokenProvider<ApplicationUser>>.Instance));

            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"))
                }
            };
            var context = new UserContextService(accessor);
            IAuditEventService audit = new AuditEventService(db, context, accessor);
            var usersService = new UsersService(users, null!, db, context, audit, null!);
            var configuration = new ConfigurationService(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppConfig:WebsiteUrl"] = "https://studio.example.com" })
                .Build());
            var email = new RecordingEmailService(emailWorks);
            return new Fixture
            {
                Db = db, Users = users, UsersService = usersService, Email = email,
                Service = new AdminUserProvisioningService(users, usersService, email, configuration, audit)
            };
        }
    }

    private sealed class RecordingEmailService(bool works) : IEmailService
    {
        public List<EmailData> Sent { get; } = [];

        public Task<bool> Send(EmailData emailDetails, CancellationToken cancellationToken)
        {
            if (works) Sent.Add(emailDetails);
            return Task.FromResult(works);
        }
    }
}
