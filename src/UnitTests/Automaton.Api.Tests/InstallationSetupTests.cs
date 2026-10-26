using Automaton.Api.Application.Commands;
using Automaton.Api.Authentication.GitHub;
using Automaton.Api.Controllers;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

using static Automaton.Api.Tests.InstallationTestFixture;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InstallationSetupTests
{
    [TestCase("Production", null, null, false)]
    [TestCase("Development", null, null, true)]
    [TestCase("Production", "server-token", "server-token", true)]
    [TestCase("Development", "server-token", null, false)]
    [TestCase("Production", "server-token", "another-token", false)]
    public void SetupAccessRequiresConfiguredTokenOutsideDevelopment(string environment, string? expected, string? supplied, bool allowed)
    {
        var configuration = Configuration(expected);
        Assert.That(InstallationSetupAccess.IsAllowed(configuration, new SetupEnvironment { EnvironmentName = environment }, supplied), Is.EqualTo(allowed));
    }

}

[TestFixture]
public sealed class InstallationCompletionTests
{
    [Test]
    public async Task OrdinaryUserDoesNotPreventCreatingFirstAdministrator()
    {
        await using var db = Database();
        db.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = "ordinary@example.invalid", PublicUsername = "ordinary" });
        await db.SaveChangesAsync();
        Assert.That(await Service(db).InstallationComplete(), Is.False);
    }

    [Test]
    public async Task ExistingAdministratorCompletesLegacyInstallation()
    {
        await using var db = Database();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = roleId, Name = UserRole.SystemAdministrator });
        db.UserRoles.Add(new ApplicationUserRole { RoleId = roleId, UserId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        Assert.That(await Service(db).InstallationComplete(), Is.True);
    }

    [Test]
    public async Task CompletionMarkerKeepsSetupClosedAfterAccountsAreRemoved()
    {
        await using var db = Database();
        db.Settings.Add(new Setting { Key = "Installation.Complete", Value = "true" });
        await db.SaveChangesAsync();
        Assert.That(await Service(db).InstallationComplete(), Is.True);
        var controller = Controller(db);
        controller.Request.Headers[InstallationSetupAccess.HeaderName] = "server-token";
        Assert.That(await controller.Post(new InstallCommand(), CancellationToken.None), Is.TypeOf<ConflictObjectResult>());
        Assert.That(await controller.Check(CancellationToken.None), Is.TypeOf<ForbidResult>());
    }

}

[TestFixture]
public sealed class InstallationEndpointTests
{
    [Test]
    public async Task InvalidTokenCannotCreateAdministratorOrReadChecks()
    {
        await using var db = Database();
        var controller = Controller(db);
        var create = (ObjectResult)await controller.Post(new InstallCommand(), CancellationToken.None);
        var check = (ObjectResult)await controller.Check(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(create.StatusCode, Is.EqualTo(403));
            Assert.That(check.StatusCode, Is.EqualTo(403));
        });
    }

}

[TestFixture]
public sealed class InstallationReadinessTests
{
    [Test]
    public async Task DistinguishesLiveChecksSavedSettingsAndExternalChecks()
    {
        await using var db = Database();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = "1",
            ["AuthenticationSettings:HMacSettings:SecretKey"] = "test-key",
            ["DataProtection:KeysPath"] = Path.GetTempPath(),
            ["AppConfig:ApiUrl"] = "https://api.example.invalid",
            ["AppConfig:WebsiteUrl"] = "https://studio.example.invalid"
        }).Build();

        var smtp = SmtpTestFixture.Service(db, file: new Automaton.Api.Config.EmailConfig());
        var checks = await InstallationReadiness.CheckAsync(db, configuration, smtp, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(checks.Single(check => check.Name == "Database").Status, Is.EqualTo("verified"));
            Assert.That(checks.Single(check => check.Name == "Message broker").Status, Is.EqualTo("error"));
            Assert.That(checks.Single(check => check.Name == "Public URLs").Status, Is.EqualTo("configured"));
            Assert.That(checks.Single(check => check.Name == "Public DNS and ports").Status, Is.EqualTo("unverified"));
            Assert.That(checks.Single(check => check.Name == "SMTP").Status, Is.EqualTo("unverified"));
        });
    }
}

[TestFixture]
public sealed class OptionalGitHubAuthenticationTests
{
    [Test]
    public async Task UnconfiguredGitHubDoesNotRegisterRemoteHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddGitHubOAuth(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(GitHubAuthenticationDefaults.Scheme);
        Assert.That(scheme, Is.Null);
    }

}

internal static class InstallationTestFixture
{
    internal static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal static InstallService Service(ApplicationDbContext db) => new(null!, null!, null!, db, NullLogger<InstallService>.Instance, null!);

    internal static IConfiguration Configuration(string? token) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Bootstrap:Token"] = token }).Build();

    internal static InstallController Controller(ApplicationDbContext db) => new(Service(db), Configuration("server-token"), new SetupEnvironment(), db, null!)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    internal sealed class SetupEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
