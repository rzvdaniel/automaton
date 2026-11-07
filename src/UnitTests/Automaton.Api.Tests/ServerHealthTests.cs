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
    public void TheHealthEndpointUsesOnlyTheServerReadPolicy()
    {
        var policy = typeof(ServerHealthController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo(AppPermissionAuthenticationOptions.ServerReadPolicy));
        Assert.That(typeof(ServerHealthController).GetMethods().SelectMany(method => method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)), Is.Empty);
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
