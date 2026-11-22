using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Config;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.ServerHealth;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ServerHealthTests
{
    private static MachineHostHealth Sample(DateTime? at = null) => new(
        at ?? DateTime.UtcNow, "Ubuntu 24.04.1 LTS", "24.04", 3600, 4, 12.5, 0.5, 0.4, 0.3,
        8_000, 2_000, 100_000, 60_000, 10, 20, 30, 40, 7, 2, true, DateTime.UtcNow.AddDays(-4), "1.0.0");

    [Test]
    public async Task MachineRunnerReport_IsStoredAndNewestIsServed()
    {
        await using var db = CreateDb();
        var (runner, key) = SeedMachineRunner(db);
        await db.SaveChangesAsync();
        var service = new HostHealthReportService(db);

        await service.ReportAsync(key, Sample(DateTime.UtcNow.AddMinutes(-2)), default);
        await service.ReportAsync(key, Sample() with { DiskAvailableBytes = 5_000 }, default);
        await service.ReportAsync(key, Sample(DateTime.UtcNow.AddMinutes(-9)) with { DiskAvailableBytes = 1 }, default);

        var latest = await service.LatestAsync(default);
        Assert.Multiple(() =>
        {
            Assert.That(latest!.DiskAvailableBytes, Is.EqualTo(5_000), "an older report never replaces a newer one");
            Assert.That(latest.SecurityUpdates, Is.EqualTo(2));
            Assert.That(runner.MachineHostHealthMeasuredAtUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task OnlyMachineManagementRunnersMayReport_AndBadValuesAreRejected()
    {
        await using var db = CreateDb();
        var (_, key) = SeedMachineRunner(db);
        var other = Guid.NewGuid();
        db.Runners.Add(new Runner { Id = Guid.NewGuid(), Name = "std", Purpose = RunnerPurposes.Managed, ApplicationProfile = "Standard", ApiKeyId = other });
        await db.SaveChangesAsync();
        var service = new HostHealthReportService(db);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReportAsync(other, Sample(), default));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReportAsync(Guid.NewGuid(), Sample(), default));
        Assert.ThrowsAsync<ArgumentException>(() => service.ReportAsync(key, Sample(DateTime.UtcNow.AddHours(-2)), default));
        Assert.ThrowsAsync<ArgumentException>(() => service.ReportAsync(key, Sample() with { DiskAvailableBytes = 999_999 }, default));
        Assert.ThrowsAsync<ArgumentException>(() => service.ReportAsync(key, Sample() with { UpdatesPending = -1 }, default));
        Assert.That(await service.LatestAsync(default), Is.Null);
    }

    [Test]
    public async Task ReportController_ReturnsNoContentForMachineRunnerAndForbiddenOtherwise()
    {
        await using var db = CreateDb();
        var (_, key) = SeedMachineRunner(db);
        await db.SaveChangesAsync();
        RunnerHostHealthController Controller(Guid id) => new(new HostHealthReportService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("api_key_id", id.ToString())], "test")) }
            }
        };

        Assert.That(await Controller(key).Report(Sample(), default), Is.TypeOf<NoContentResult>());
        Assert.That(((ObjectResult)await Controller(Guid.NewGuid()).Report(Sample(), default)).StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task HealthCombinesHostFiguresVersionsRunnersAndBackup()
    {
        await using var db = CreateDb();
        var (_, key) = SeedMachineRunner(db);
        await db.SaveChangesAsync();
        await new HostHealthReportService(db).ReportAsync(key, Sample(), default);
        var memory = new MemoryCache(new MemoryCacheOptions());
        memory.Set(nameof(LatestReleaseClient), "0.2.9");
        var service = Service(db, memory, "0.2.6");

        var health = await service.GetAsync(DateTime.UtcNow, default);

        Assert.Multiple(() =>
        {
            Assert.That(health.Server, Is.EqualTo(new ServerVersionInfo("0.2.6", "0.2.9", true)));
            Assert.That(health.Host!.OsName, Is.EqualTo("Ubuntu 24.04.1 LTS"));
            Assert.That(health.HostAgeSeconds, Is.LessThan(30));
            Assert.That(health.Runners.Single().Name, Is.EqualTo("machine"));
            Assert.That(health.Certificates, Is.Empty, "plain-http URLs have no certificate to read");
            Assert.That(health.Backup.Available, Is.False);
        });
    }

    [Test]
    public async Task EachRunnerListsItsOwnHostFigures_AndARunnerThatNeverReportedHasNone()
    {
        await using var db = CreateDb();
        var (_, key) = SeedMachineRunner(db);
        db.Runners.Add(new Runner
        {
            Id = Guid.NewGuid(), Name = "silent", Purpose = RunnerPurposes.Managed, Status = "offline",
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement, InstallationId = "sh_silent", ApiKeyId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        await new HostHealthReportService(db).ReportAsync(key, Sample(DateTime.UtcNow.AddMinutes(-3)) with { DiskAvailableBytes = 4_242 }, default);
        var memory = new MemoryCache(new MemoryCacheOptions());
        memory.Set(nameof(LatestReleaseClient), (string?)null);

        var health = await Service(db, memory, null).GetAsync(DateTime.UtcNow, default);

        var reported = health.Runners.Single(item => item.Name == "machine");
        var silent = health.Runners.Single(item => item.Name == "silent");
        Assert.Multiple(() =>
        {
            Assert.That(reported.Host!.DiskAvailableBytes, Is.EqualTo(4_242));
            Assert.That(reported.HostAgeSeconds, Is.InRange(150, 300));
            Assert.That(silent.Host, Is.Null);
            Assert.That(silent.HostAgeSeconds, Is.Null);
        });
    }

    [Test]
    public async Task HealthWithoutAReportingRunnerHasNoHostFigures()
    {
        await using var db = CreateDb();
        var memory = new MemoryCache(new MemoryCacheOptions());
        memory.Set(nameof(LatestReleaseClient), (string?)null);

        var health = await Service(db, memory, null).GetAsync(DateTime.UtcNow, default);

        Assert.That(health.Host, Is.Null);
        Assert.That(health.Server.UpdateAvailable, Is.Null);
    }

    [Test]
    public void NewestStableTag_IgnoresPreviewsAndOrdersNumerically()
        => Assert.That(LatestReleaseClient.NewestStable(["latest", "server-v0.2.9", "server-v0.2.10", "server-v0.3.0-rc1", null]), Is.EqualTo("0.2.10"));

    [Test]
    public void CertificateDaysRemainingRoundsDown()
        => Assert.That(CertificateExpiryProbe.DaysRemaining(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)), Is.EqualTo(29));

    [Test]
    public async Task OnlyAServerReadCredentialOrASystemAdministratorPassesThePolicy()
    {
        var services = new ServiceCollection().AddLogging()
            .AddAuthorization(options => AppPermissionAuthenticationOptions.AddPermissionPolicy(options,
                AppPermissionAuthenticationOptions.ServerReadPolicy, AppPermissionApproval.ServerRead)).BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        static ClaimsPrincipal App(params string[] permissions) => new(new ClaimsIdentity(
            permissions.Select(permission => new Claim("app_permission", permission)), "AppPermission"));

        async Task<bool> Allowed(ClaimsPrincipal user) =>
            (await authorization.AuthorizeAsync(user, AppPermissionAuthenticationOptions.ServerReadPolicy)).Succeeded;

        Assert.That(await Allowed(App(AppPermissionApproval.ServerRead)), Is.True);
        Assert.That(await Allowed(App(AppPermissionApproval.BackupsRead, AppPermissionApproval.ServerRead)), Is.True);
        Assert.That(await Allowed(App(AppPermissionApproval.BackupsRead)), Is.False, "a backups key cannot read server health");
        Assert.That(await Allowed(App()), Is.False);
        Assert.That(await Allowed(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, UserRole.SystemAdministrator)], "Identity.Application"))), Is.True);
        Assert.That(await Allowed(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, UserRole.Administrator)], "Identity.Application"))), Is.False);
        Assert.That(await Allowed(new ClaimsPrincipal(new ClaimsIdentity())), Is.False, "anonymous");
    }

    [Test]
    public async Task EachRunnerListsItsAppsBySize_WithNames_AndNegativeAppFiguresAreRejected()
    {
        await using var db = CreateDb();
        var (_, key) = SeedMachineRunner(db);
        var small = Guid.NewGuid();
        var large = Guid.NewGuid();
        db.WebApps.Add(new WebApp { Id = large, Name = "e2e-big", Created = DateTime.UtcNow, Updated = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var reports = new HostHealthReportService(db);
        var apps = new[] { new AppDiskUsage(small, 1, 2, 3, 4, 5), new AppDiskUsage(large, 100, 200, 0, 50, 1_000, 12.5, 4096) };
        await reports.ReportAsync(key, Sample() with { AppDisk = apps, DockerUnusedImagesBytes = 77 }, default);
        var memory = new MemoryCache(new MemoryCacheOptions());
        memory.Set(nameof(LatestReleaseClient), (string?)null);

        var health = await Service(db, memory, null).GetAsync(DateTime.UtcNow, default);

        var runner = health.Runners.Single();
        Assert.Multiple(() =>
        {
            Assert.That(runner.Apps!.Select(item => item.AppId), Is.EqualTo(new[] { large, small }), "largest first");
            Assert.That(runner.Apps![0].AppName, Is.EqualTo("e2e-big"));
            Assert.That(runner.Apps[0].TotalBytes, Is.EqualTo(1_350));
            Assert.That((runner.Apps[0].CpuPercent, runner.Apps[0].MemoryBytes), Is.EqualTo(((double?)12.5, (long?)4096)));
            Assert.That(runner.Apps[1].CpuPercent, Is.Null);
            Assert.That(runner.Host!.DockerUnusedImagesBytes, Is.EqualTo(77));
            Assert.That(runner.Host.AppDisk, Is.Null, "the app list is not repeated inside the host figures");
        });
        Assert.ThrowsAsync<ArgumentException>(() => reports.ReportAsync(
            key, Sample(DateTime.UtcNow.AddSeconds(4)) with { AppDisk = [new AppDiskUsage(small, 1, null, null, null, null, 1, -5)] }, default));
        Assert.ThrowsAsync<ArgumentException>(() => reports.ReportAsync(
            key, Sample(DateTime.UtcNow.AddSeconds(5)) with { AppDisk = [new AppDiskUsage(small, -1, null, null, null, null)] }, default));
    }

    [Test]
    public void TheHealthEndpointUsesOnlyTheServerReadPolicy()
    {
        var policy = typeof(ServerHealthController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo(AppPermissionAuthenticationOptions.ServerReadPolicy));
        Assert.That(typeof(ServerHealthController).GetMethods().SelectMany(method => method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)), Is.Empty);
    }

    [Test]
    public async Task Fail2BanIsServedToSecurityReadOnly_AndNeverInsideServerHealth()
    {
        await using var db = CreateDb();
        var (_, key) = SeedMachineRunner(db);
        await db.SaveChangesAsync();
        var reports = new HostHealthReportService(db);
        var now = DateTime.UtcNow;
        var status = new Fail2BanStatus(true,
        [
            new("sshd", true, 3, 12, [new("203.0.113.9", now.AddMinutes(-5), now.AddMinutes(55)), new("198.51.100.2", now.AddHours(-3), now.AddMinutes(-1))]),
            new("recidive", true, 0, 1, [new("2001:db8::1", now.AddHours(-1), null)])
        ]);
        await reports.ReportAsync(key, Sample() with { Fail2Ban = status }, default);
        var memory = new MemoryCache(new MemoryCacheOptions());
        memory.Set(nameof(LatestReleaseClient), (string?)null);

        var security = await new Fail2BanReadService(reports).GetAsync(now, default);
        var health = await Service(db, memory, null).GetAsync(now, default);

        Assert.Multiple(() =>
        {
            Assert.That(security.Reported, Is.True);
            Assert.That(security.Running, Is.True);
            Assert.That(security.BannedLastHour, Is.EqualTo(3));
            Assert.That(security.BannedLast24Hours, Is.EqualTo(13));
            Assert.That(security.Jails[0].CurrentBans.Select(ban => ban.Ip), Is.EqualTo(new[] { "203.0.113.9" }), "an expired ban is not current");
            Assert.That(security.Jails[1].CurrentBans.Single().ExpiresAtUtc, Is.Null, "permanent ban");
            Assert.That(health.Host!.Fail2Ban, Is.Null, "server:read does not carry fail2ban");
            Assert.That(health.Runners.Single().Host!.Fail2Ban, Is.Null);
        });
        Assert.ThrowsAsync<ArgumentException>(() => reports.ReportAsync(key,
            Sample(DateTime.UtcNow.AddSeconds(5)) with { Fail2Ban = new(true, [new("x", true, 0, 0, [new("not-an-ip", now, null)])]) }, default));
    }

    [Test]
    public async Task Fail2BanIsNotReportedBeforeARunnerReadsIt()
    {
        await using var db = CreateDb();
        var (_, key) = SeedMachineRunner(db);
        await db.SaveChangesAsync();
        var reports = new HostHealthReportService(db);
        Assert.That((await new Fail2BanReadService(reports).GetAsync(DateTime.UtcNow, default)).Reported, Is.False, "no report yet");
        await reports.ReportAsync(key, Sample(), default);
        Assert.That((await new Fail2BanReadService(reports).GetAsync(DateTime.UtcNow, default)).Reported, Is.False, "the Runner could not read fail2ban");
    }

    [Test]
    public async Task OnlyASecurityReadCredentialOrASystemAdministratorPassesTheSecurityPolicy()
    {
        var services = new ServiceCollection().AddLogging()
            .AddAuthorization(options => AppPermissionAuthenticationOptions.AddPermissionPolicy(options,
                AppPermissionAuthenticationOptions.SecurityReadPolicy, AppPermissionApproval.SecurityRead)).BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        static ClaimsPrincipal App(params string[] permissions) => new(new ClaimsIdentity(
            permissions.Select(permission => new Claim("app_permission", permission)), "AppPermission"));
        async Task<bool> Allowed(ClaimsPrincipal user) =>
            (await authorization.AuthorizeAsync(user, AppPermissionAuthenticationOptions.SecurityReadPolicy)).Succeeded;

        Assert.That(await Allowed(App(AppPermissionApproval.SecurityRead)), Is.True);
        Assert.That(await Allowed(App(AppPermissionApproval.ServerRead, AppPermissionApproval.BackupsRead)), Is.False, "server:read is not security:read");
        Assert.That(await Allowed(App()), Is.False);
        Assert.That(await Allowed(new ClaimsPrincipal(new ClaimsIdentity())), Is.False, "anonymous");
        var policy = typeof(ServerSecurityController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo(AppPermissionAuthenticationOptions.SecurityReadPolicy));
        Assert.That(ApplicationContractValidator.SupportedPermissions, Does.Contain("security:read"));
    }

    private sealed class FakeCommands : Automaton.Api.ServerCleanup.IRunnerMaintenanceCommands
    {
        public List<(Guid Runner, string Kind, string Payload)> Sent { get; } = [];
        public Task<System.Text.Json.JsonElement> SendAsync(Guid runnerId, Guid actor, AppActionCommand command, TimeSpan timeout, CancellationToken token)
        {
            Sent.Add((runnerId, command.Kind, command.Payload.ToString()));
            return Task.FromResult(System.Text.Json.JsonSerializer.SerializeToElement(new Fail2BanActionResult("203.0.113.9", ["sshd"])));
        }
    }

    private sealed class FakeAudit : Automaton.Api.Services.Interfaces.IAuditEventService
    {
        public List<(string Action, string Message, string Metadata)> Events { get; } = [];
        public Task WriteAsync(Automaton.Api.Models.AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
        {
            Events.Add((action, message, System.Text.Json.JsonSerializer.Serialize(metadata)));
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task UnbanAndAllowlist_GoToTheRunnerThatReportsFail2Ban_AndAreAudited()
    {
        await using var db = CreateDb();
        var (runner, key) = SeedMachineRunner(db);
        await db.SaveChangesAsync();
        var reports = new HostHealthReportService(db);
        var commands = new FakeCommands();
        var audit = new FakeAudit();
        var manage = new Fail2BanManageService(reports, commands, audit);
        Assert.ThrowsAsync<InvalidOperationException>(() => manage.UnbanAsync(Guid.NewGuid(), Guid.NewGuid(), "203.0.113.9", default), "nothing reported yet");

        await reports.ReportAsync(key, Sample() with { Fail2Ban = new(true, []) }, default);
        var app = Guid.NewGuid();
        await manage.UnbanAsync(app, Guid.NewGuid(), "203.0.113.9", default);
        await manage.AllowlistAsync(app, Guid.NewGuid(), "2001:0db8:0:0:0:0:0:1", default);

        Assert.Multiple(() =>
        {
            Assert.That(commands.Sent.Select(item => item.Kind), Is.EqualTo(new[] { "fail2ban-unban", "fail2ban-allowlist" }));
            Assert.That(commands.Sent.All(item => item.Runner == runner.Id), Is.True);
            Assert.That(commands.Sent[1].Payload, Does.Contain("2001:db8::1"), "the canonical address is sent");
            Assert.That(audit.Events, Has.Count.EqualTo(2));
            Assert.That(audit.Events[0].Message, Does.Contain("unban of 203.0.113.9"));
            Assert.That(audit.Events[1].Metadata, Does.Contain("fail2ban-allowlist"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-an-ip")]
    [TestCase("203.0.113.0/24")]
    [TestCase("1.2.3.4 5.6.7.8")]
    public async Task AnInvalidAddress_IsRefusedWithoutAuditOrCommand(string? ip)
    {
        await using var db = CreateDb();
        var commands = new FakeCommands();
        var audit = new FakeAudit();
        var manage = new Fail2BanManageService(new HostHealthReportService(db), commands, audit);
        Assert.ThrowsAsync<ArgumentException>(() => manage.UnbanAsync(Guid.NewGuid(), Guid.NewGuid(), ip, default));
        Assert.That(commands.Sent, Is.Empty);
        Assert.That(audit.Events, Is.Empty);
    }

    [Test]
    public async Task OnlyASecurityManageCredentialOrASystemAdministratorPassesTheManagePolicy()
    {
        var services = new ServiceCollection().AddLogging()
            .AddAuthorization(options => AppPermissionAuthenticationOptions.AddPermissionPolicy(options,
                AppPermissionAuthenticationOptions.SecurityManagePolicy, AppPermissionApproval.SecurityManage)).BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        static ClaimsPrincipal App(params string[] permissions) => new(new ClaimsIdentity(
            permissions.Select(permission => new Claim("app_permission", permission)), "AppPermission"));
        async Task<bool> Allowed(ClaimsPrincipal user) =>
            (await authorization.AuthorizeAsync(user, AppPermissionAuthenticationOptions.SecurityManagePolicy)).Succeeded;

        Assert.That(await Allowed(App(AppPermissionApproval.SecurityManage)), Is.True);
        Assert.That(await Allowed(App(AppPermissionApproval.SecurityRead)), Is.False, "security:read cannot unban");
        Assert.That(await Allowed(App()), Is.False);
        var policy = typeof(ServerSecurityManageController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo(AppPermissionAuthenticationOptions.SecurityManagePolicy));
        Assert.That(ApplicationContractValidator.SupportedPermissions, Does.Contain("security:manage"));
    }

    [Test]
    public void AnAppWithoutAnAdministratorViewerToken_GetsForbidden()
    {
        var appId = Guid.NewGuid();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", appId.ToString()), new Claim("app_permission", AppPermissionApproval.SecurityManage)], "AppPermission"))
        };
        var tokens = new ViewerTokenService(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("security-manage-test"));
        var controller = new ServerSecurityManageController(null!, tokens, null!) { ControllerContext = new() { HttpContext = http } };
        var result = controller.Unban(new("203.0.113.9"), default).Result;
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    private static ServerHealthService Service(ApplicationDbContext db, IMemoryCache memory, string? version)
    {
        var values = new Dictionary<string, string?>
        {
            ["Installation:StatusDirectory"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ["AppConfig:WebsiteUrl"] = "http://studio.test"
        };
        if (version is not null) values["Installation:Version"] = version;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServerHealthService(db, configuration, new ConfigurationService(configuration),
            Options.Create(new HostedRunnerOptions { ServerUrl = "http://api.test" }),
            new HostHealthReportService(db), new LatestReleaseClient(null!, memory), new CertificateExpiryProbe(memory));
    }

    private static (Runner Runner, Guid ApiKeyId) SeedMachineRunner(ApplicationDbContext db)
    {
        var key = Guid.NewGuid();
        var runner = new Runner
        {
            Id = Guid.NewGuid(), Name = "machine", Purpose = RunnerPurposes.Managed, Status = "online",
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement, InstallationId = "sh_" + Guid.NewGuid().ToString("N"), ApiKeyId = key
        };
        db.Runners.Add(runner);
        return (runner, key);
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
