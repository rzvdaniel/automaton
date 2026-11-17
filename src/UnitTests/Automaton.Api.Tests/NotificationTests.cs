using System.Security.Claims;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Home;
using Automaton.Api.Notifications;
using Automaton.Api.ServerHealth;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Automaton.Api.Tests;

/// <summary>The notification feed: role filtering, severity, grouping and acknowledgement.</summary>
[TestFixture]
public sealed class NotificationTests
{
    private sealed class StubNeedsYou : HomeNeedsYouService
    {
        public StubNeedsYou() : base(null!, null!, null!, null!, null!, null!) { }
        public override Task<List<HomeCard>> ServerCardsAsync(CancellationToken token)
            => Task.FromResult(new List<HomeCard>
            {
                new(HomeCardKeys.ServerUpdate, HomeCardKeys.ServerUpdate, "warn", "Update the server", "9.9.9 is available.", "Open server", "/server", false),
                new(HomeCardKeys.NoSmtp, HomeCardKeys.NoSmtp, "info", "Set up email", "x", "Open", "/settings", true),
            });
    }

    private sealed class FakeNotifications(ApplicationDbContext db, ResourceAuthorizationService authorization, int? certificateDays)
        : NotificationService(db, authorization, new StubNeedsYou(), null!, new ConfigurationBuilder().Build())
    {
        protected override Task<IReadOnlyList<CertificateInfo>> ReadCertificatesAsync(CancellationToken token)
            => Task.FromResult<IReadOnlyList<CertificateInfo>>(certificateDays is { } days
                ? [new("test.example", DateTime.UtcNow.AddDays(days), days, null), new("fine.example", DateTime.UtcNow.AddDays(80), 80, null)]
                : []);
    }

    private static NotificationsController Create(Guid userId, ApplicationDbContext db, int? certificateDays = null)
    {
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(userId));
        return new NotificationsController(new FakeNotifications(db, authorization, certificateDays))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(userId == Guid.Empty ? [] : [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")) } }
        };
    }

    private static void MakeSystemAdministrator(ApplicationDbContext db, Guid userId)
    {
        var role = db.Roles.FirstOrDefault(item => item.Name == UserRole.SystemAdministrator)
            ?? db.Roles.Add(new ApplicationRole { Id = Guid.NewGuid(), Name = UserRole.SystemAdministrator, NormalizedName = "SYSADMIN" }).Entity;
        db.UserRoles.Add(new ApplicationUserRole { UserId = userId, RoleId = role.Id });
        db.SaveChanges();
    }

    private static void AddRunner(ApplicationDbContext db, string name, bool online)
        => db.Runners.Add(new Runner
        {
            Id = Guid.NewGuid(), Name = name, Purpose = "Managed", RunnerUsers = [], ApiKeyId = Guid.NewGuid(),
            ConnectionId = online ? "c" : null, LastHeartbeatAtUtc = online ? DateTime.UtcNow : DateTime.UtcNow.AddHours(-2),
        });

    private static async Task<NotificationItem[]> List(NotificationsController controller)
        => ((NotificationListResponse)((OkObjectResult)(await controller.Get(default)).Result!).Value!).Notifications.ToArray();

    [Test]
    public async Task AdministratorsSeeServerNoticesAndMembersDoNot()
    {
        Guid admin = Guid.NewGuid(), member = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        MakeSystemAdministrator(db, admin);
        AddRunner(db, "stopped", online: false);
        db.SaveChanges();

        var adminKinds = (await List(Create(admin, db, certificateDays: 10))).Select(item => item.Kind).ToArray();
        Assert.That(adminKinds, Is.EquivalentTo(new[] { "runner-offline", "certificate-expiring", "server-update" }));
        Assert.That(await List(Create(member, db, certificateDays: 10)), Is.Empty, "server notices are for system administrators");
    }

    [Test]
    public async Task SeverityFollowsHowBadItIsAndRepeatsAreGrouped()
    {
        var admin = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        MakeSystemAdministrator(db, admin);
        AddRunner(db, "a", online: false);
        AddRunner(db, "b", online: false);
        AddRunner(db, "c", online: true);
        db.SaveChanges();

        var warning = (await List(Create(admin, db, certificateDays: 10))).ToDictionary(item => item.Kind);
        Assert.That(warning["runner-offline"].Severity, Is.EqualTo("warning"), "one Runner is still connected");
        Assert.That(warning["runner-offline"].Count, Is.EqualTo(2));
        Assert.That(warning["runner-offline"].Subjects, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(warning["certificate-expiring"].Severity, Is.EqualTo("warning"));
        Assert.That(warning["certificate-expiring"].Subjects.Single(), Does.StartWith("test.example"), "only the expiring host is named");

        Assert.That((await List(Create(admin, db, certificateDays: 2))).Single(item => item.Kind == "certificate-expiring").Severity, Is.EqualTo("critical"));
        Assert.That((await List(Create(admin, db, certificateDays: 40))).Select(item => item.Kind), Has.No.Member("certificate-expiring"));
    }

    [Test]
    public async Task AcknowledgingHidesAWarningForThatPersonUntilItChanges()
    {
        Guid admin = Guid.NewGuid(), other = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        MakeSystemAdministrator(db, admin);
        MakeSystemAdministrator(db, other);

        var feed = Create(admin, db, certificateDays: 10);
        Assert.That(await feed.Acknowledge("certificate-expiring", default), Is.TypeOf<NoContentResult>());
        Assert.That((await List(feed)).Select(item => item.Kind), Has.No.Member("certificate-expiring"));
        Assert.That((await List(Create(other, db, certificateDays: 10))).Select(item => item.Kind), Has.Member("certificate-expiring"), "acknowledgement is personal");
        Assert.That(await feed.Acknowledge("nonsense", default), Is.TypeOf<NotFoundResult>());

        // It is about something else now (a second host), so it comes back.
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(admin));
        var changed = new NotificationsController(new TwoHostNotifications(db, authorization)) { ControllerContext = feed.ControllerContext };
        Assert.That((await List(changed)).Select(item => item.Kind), Has.Member("certificate-expiring"));
    }

    private sealed class TwoHostNotifications(ApplicationDbContext db, ResourceAuthorizationService authorization)
        : NotificationService(db, authorization, new StubNeedsYou(), null!, new ConfigurationBuilder().Build())
    {
        protected override Task<IReadOnlyList<CertificateInfo>> ReadCertificatesAsync(CancellationToken token)
            => Task.FromResult<IReadOnlyList<CertificateInfo>>([new("test.example", DateTime.UtcNow.AddDays(9), 9, null), new("second.example", DateTime.UtcNow.AddDays(9), 9, null)]);
    }

    [Test]
    public async Task ACriticalNoticeCannotBeAcknowledged()
    {
        var admin = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        MakeSystemAdministrator(db, admin);
        AddRunner(db, "only", online: false);
        db.SaveChanges();

        var feed = Create(admin, db);
        Assert.That(await feed.Acknowledge("runner-offline", default), Is.TypeOf<ConflictObjectResult>());
        Assert.That((await List(feed)).Single(item => item.Kind == "runner-offline"), Has.Property("Severity").EqualTo("critical").And.Property("Acknowledgeable").False);
        Assert.That(db.UserNotificationAcks.Count(), Is.Zero);
    }

    [Test]
    public async Task ARunnerThatComesBackClearsItsNotice()
    {
        var admin = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        MakeSystemAdministrator(db, admin);
        AddRunner(db, "only", online: true);
        db.SaveChanges();
        Assert.That((await List(Create(admin, db))).Select(item => item.Kind), Has.No.Member("runner-offline"));
    }

    [Test]
    public async Task APersonSeesFailingAppsTheyCanChangeAndAnAnonymousCallerIsRejected()
    {
        var owner = Guid.NewGuid();
        var db = AppAuthorizationTestData.CreateDbContext();
        Assert.That((await Create(Guid.Empty, db).Get(default)).Result, Is.TypeOf<UnauthorizedResult>());
        Assert.That(await Create(Guid.Empty, db).Acknowledge("x", default), Is.TypeOf<UnauthorizedResult>());
        Assert.That(await List(Create(owner, db)), Is.Empty);
    }

    [Test]
    public void TheFeedIsForSignedInPeopleAndNotForScopedKeys()
    {
        var policy = typeof(NotificationsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo("DeveloperApiAccess"));
    }
}
