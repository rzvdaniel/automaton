using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class NotifyEmailTests
{
    private readonly Guid appId = Guid.NewGuid(), projectId = Guid.NewGuid();
    private Guid owner, admin, member, stranger;
    private ApplicationDbContext db = null!;
    private RecordingSmtp smtp = null!;
    private RecordingAudit audit = null!;
    private IMemoryCache memory = null!;

    [SetUp]
    public async Task SetUp()
    {
        db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        owner = AddUser("owner@example.test");
        admin = AddUser("admin@example.test");
        member = AddUser("member@example.test");
        stranger = AddUser("stranger@example.test");
        var role = new ApplicationRole { Id = Guid.NewGuid(), Name = UserRole.SystemAdministrator, NormalizedName = "SYSADMIN" };
        db.Roles.Add(role);
        db.UserRoles.Add(new ApplicationUserRole { UserId = admin, RoleId = role.Id });
        AppAuthorizationTestData.SeedApp(db, appId, owner, "notify-app");
        db.WebApps.Single(app => app.Id == appId).Name = "Server Console";
        db.ProjectApps.Add(new ProjectApp { ProjectId = projectId, WebAppId = appId });
        db.ProjectMembers.Add(new ProjectMember { ProjectId = projectId, UserId = member, IsActive = true });
        await db.SaveChangesAsync();
        smtp = new RecordingSmtp(db);
        audit = new RecordingAudit();
        memory = new MemoryCache(new MemoryCacheOptions());
    }

    [TearDown]
    public async Task TearDown()
    {
        memory.Dispose();
        await db.DisposeAsync();
    }

    private Guid AddUser(string email)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = id, Email = email, UserName = email, PublicUsername = email.Split((char)64)[0] });
        return id;
    }

    private NotifyEmailService Service() => new(db, smtp, memory, audit);

    private static NotifyEmailRequest Request(params string[] to) => new(to, "Disk almost full", "Only 2 GB left.");

    [Test]
    public async Task GroupsReachOwnerAdministratorsAndProjectMembersAndNobodyElse()
    {
        await Service().SendAsync(appId, Request("owner", "administrators", "project"), default);

        Assert.That(smtp.Recipients, Is.EquivalentTo(new[] { "owner@example.test", "admin@example.test", "member@example.test" }));
        Assert.That(smtp.Subject, Is.EqualTo("[Server Console] Disk almost full"));
    }

    [Test]
    public async Task AnAddressOnTheAllowedListIsEmailedAndAnyOtherIsRefused()
    {
        await Service().SetAllowedAsync(["Ops@Example.test"], default);

        var sent = await Service().SendAsync(appId, Request("ops@example.test"), default);
        Assert.That(sent.Sent, Is.EqualTo(1));

        smtp.Recipients = [];
        var error = Assert.ThrowsAsync<NotifyEmailException>(() => Service().SendAsync(appId, Request("owner", "anyone@example.test"), default))!;
        Assert.That(error.Failure, Is.EqualTo(NotifyEmailFailure.NotAllowed));
        Assert.That(smtp.Recipients, Is.Empty, "nothing is sent when one address is refused");
        Assert.That(audit.Events.Select(item => item.EventType), Does.Contain("AppEmailRefused"));
    }

    [Test]
    public async Task AUserAddressTypedByTheAppIsNotAllowedUnlessListed()
    {
        var error = Assert.ThrowsAsync<NotifyEmailException>(() => Service().SendAsync(appId, Request("stranger@example.test"), default))!;
        Assert.That(error.Failure, Is.EqualTo(NotifyEmailFailure.NotAllowed));
        await Task.CompletedTask;
    }

    [Test]
    public void WhenSmtpIsNotConfiguredTheCallFailsClearlyAndSendsNothing()
    {
        smtp.Configured = false;

        var error = Assert.ThrowsAsync<NotifyEmailException>(() => Service().SendAsync(appId, Request("owner"), default))!;

        Assert.That(error.Failure, Is.EqualTo(NotifyEmailFailure.SmtpNotConfigured));
        Assert.That(error.Message, Does.Contain("SMTP"));
        Assert.That(smtp.Recipients, Is.Empty);
    }

    [Test]
    public async Task EachAppIsLimitedPerHourAndOtherAppsAreNotAffected()
    {
        for (var i = 0; i < NotifyEmailService.MaxSendsPerHour; i++) await Service().SendAsync(appId, Request("owner"), default);

        var error = Assert.ThrowsAsync<NotifyEmailException>(() => Service().SendAsync(appId, Request("owner"), default))!;
        Assert.That(error.Failure, Is.EqualTo(NotifyEmailFailure.RateLimited));
        Assert.That(error.RetryAfterSeconds, Is.GreaterThan(0));

        var other = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, other, owner, "other-app");
        Assert.That((await Service().SendAsync(other, Request("owner"), default)).Sent, Is.EqualTo(1));
    }

    [Test]
    public async Task ASuccessfulSendIsAuditedWithoutTheBodyOrTheAddresses()
    {
        await Service().SendAsync(appId, Request("administrators"), default);

        var sent = audit.Events.Single(item => item.EventType == "AppEmailSent");
        Assert.That(sent.EntityId, Is.EqualTo(appId));
        var written = System.Text.Json.JsonSerializer.Serialize(sent.Metadata) + sent.Message;
        Assert.That(written, Does.Not.Contain("Only 2 GB left.").And.Not.Contain("example.test"));
    }

    [Test]
    public async Task ProjectGroupForAnAppWithoutAProjectHasNoRecipients()
    {
        var loner = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, loner, owner, "loner");
        var error = Assert.ThrowsAsync<NotifyEmailException>(() => Service().SendAsync(loner, Request("project"), default))!;
        Assert.That(error.Failure, Is.EqualTo(NotifyEmailFailure.NoRecipients));
        await Task.CompletedTask;
    }

    [TestCase(null, "s", "t")]
    [TestCase("owner", "", "t")]
    [TestCase("owner", "s", "  ")]
    public void MissingPartsAreRejected(string? to, string subject, string text)
    {
        var error = Assert.ThrowsAsync<NotifyEmailException>(() => Service().SendAsync(appId,
            new(to is null ? null : [to], subject, text), default))!;
        Assert.That(error.Failure, Is.EqualTo(NotifyEmailFailure.Invalid));
    }

    [Test]
    public async Task LineBreaksInTheSubjectAreRemovedAndListsOrNamesAreNotAddresses()
    {
        await Service().SendAsync(appId, new(["owner"], "Hi\r\nBcc: x@example.test", "t"), default);
        Assert.That(smtp.Subject, Is.EqualTo("[Server Console] Hi Bcc: x@example.test"));
        foreach (var tricky in new[] { "owner,administrators", "Bob <owner@example.test>", "owner@example.test, admin@example.test" })
            Assert.That(Assert.ThrowsAsync<NotifyEmailException>(() => Service().SendAsync(appId, Request(tricky), default))!.Failure,
                Is.EqualTo(NotifyEmailFailure.NotAllowed), tricky);
    }

    [Test]
    public void AnUnknownAppGets404FromTheService()
        => Assert.ThrowsAsync<KeyNotFoundException>(() => Service().SendAsync(Guid.NewGuid(), Request("owner"), default));

    [Test]
    public async Task TheEndpointMapsEachOutcomeToItsStatusCode()
    {
        var controller = Controller(appId);
        Assert.That(await controller.Send(Request("owner"), default), Is.TypeOf<OkObjectResult>());
        Assert.That(Status(await Controller(appId).Send(Request("x@example.test"), default)), Is.EqualTo(403));
        Assert.That(Status(await Controller(appId).Send(new(null, "s", "t"), default)), Is.EqualTo(400));
        Assert.That(Status(await Controller(Guid.NewGuid()).Send(Request("owner"), default)), Is.EqualTo(404), "unknown app");
        smtp.Configured = false;
        Assert.That(Status(await Controller(appId).Send(Request("owner"), default)), Is.EqualTo(409));
        smtp.Configured = true;
        for (var i = 0; i < NotifyEmailService.MaxSendsPerHour; i++) await Controller(appId).Send(Request("owner"), default);
        Assert.That(Status(await Controller(appId).Send(Request("owner"), default)), Is.EqualTo(429));
    }

    [Test]
    public async Task OnlyAnAppWithTheApprovedPermissionPassesThePolicy()
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization(options =>
            AppPermissionAuthenticationOptions.AddAppOnlyPermissionPolicy(options, "p", "notify:email")).BuildServiceProvider();
        var policy = (await services.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync("p"))!;
        var service = services.GetRequiredService<IAuthorizationService>();
        static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

        Assert.Multiple(async () =>
        {
            Assert.That((await service.AuthorizeAsync(User(new Claim("app_id", Guid.NewGuid().ToString()), new Claim("app_permission", "notify:email")), null, policy)).Succeeded, Is.True, "approved app");
            Assert.That((await service.AuthorizeAsync(User(new Claim("app_id", Guid.NewGuid().ToString()), new Claim("app_permission", "server:read")), null, policy)).Succeeded, Is.False, "app with another permission");
            Assert.That((await service.AuthorizeAsync(User(new Claim(ClaimTypes.Role, UserRole.SystemAdministrator)), null, policy)).Succeeded, Is.False, "administrator");
            Assert.That((await service.AuthorizeAsync(User(new Claim(ClaimTypes.Role, UserRole.SystemAdministrator), new Claim("api_key_scope", "Developer")), null, policy)).Succeeded, Is.False, "administrator API key");
            Assert.That(policy.AuthenticationSchemes, Is.EqualTo(new[] { AppPermissionAuthenticationOptions.Scheme }));
        });
    }

    [Test]
    public void TheEndpointUsesOnlyThePolicyAndOnlyAdministratorsEditTheAllowedList()
    {
        Assert.That(typeof(NotifyEmailController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy,
            Is.EqualTo(AppPermissionAuthenticationOptions.NotifyEmailPolicy));
        Assert.That(typeof(AdminNotifyRecipientsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles,
            Is.EqualTo(UserRole.SystemAdministrator));
    }

    [Test]
    public async Task AllowedListRejectsInvalidAddresses()
    {
        Assert.ThrowsAsync<ArgumentException>(() => Service().SetAllowedAsync(["not an address"], default));
        await Service().SetAllowedAsync(["a@example.test", "A@example.test"], default);
        Assert.That(await Service().AllowedAsync(default), Is.EqualTo(new[] { "a@example.test" }));
    }

    [Test]
    public void NotifyEmailIsASupportedPermission()
        => Assert.That(Automaton.Core.Models.ApplicationContractValidator.SupportedPermissions, Does.Contain("notify:email"));

    private NotifyEmailController Controller(Guid app)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", app.ToString())], "test")) };
        return new NotifyEmailController(Service()) { ControllerContext = new() { HttpContext = context } };
    }

    private static int Status(IActionResult result) => result switch
    {
        ObjectResult { StatusCode: { } code } => code,
        ObjectResult => 200,
        StatusCodeResult status => status.StatusCode,
        _ => throw new AssertionException($"Unexpected {result}")
    };

    private sealed class RecordingSmtp(ApplicationDbContext db, EmailConfig? config = null) : SmtpSettingsService(db,
        Options.Create(config ??= new EmailConfig { EmailHost = "smtp.example.test", EmailPort = 587, EmailAccount = "a", EmailPassword = "p" }),
        new EphemeralDataProtectionProvider())
    {
        public bool Configured { get => config!.EmailHost.Length > 0; set => config!.EmailHost = value ? "smtp.example.test" : ""; }
        public IReadOnlyCollection<string> Recipients { get; set; } = [];
        public string? Subject { get; private set; }

        public override Task SendPlainAsync(IReadOnlyCollection<string> recipients, string subject, string text, CancellationToken token)
        {
            Recipients = recipients;
            Subject = subject;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAudit : IAuditEventService
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) { Events.Add(request); return Task.CompletedTask; }
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
            => WriteAsync(new AuditEventWriteRequest { Action = action, EntityType = entityType, EntityId = entityId, Message = message, Metadata = metadata });
    }
}
