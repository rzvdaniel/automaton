using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class MachineStaticRouteTests
{
    [Test]
    public async Task RefreshAsync_PreservesPerSiteHostsAndAuthenticationWithoutHostedRunnerRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-static-route-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var runnerId = Guid.NewGuid();
        db.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "Machine management",
            Status = "connected",
            Purpose = "Managed",
            ApplicationProfile = "MachineManagement"
        });
        var first = AddSite(db, runnerId, "first-static-site");
        var second = AddSite(db, runnerId, "second-static-site");
        await db.SaveChangesAsync();

        try
        {
            await CreateRouteService(db, root).RefreshAsync();
            var routes = await File.ReadAllTextAsync(Path.Combine(root, "managed-runtime-routes.yml"));

            Assert.Multiple(() =>
            {
                Assert.That(routes, Does.Contain("Host(`first-static-site.apps.automaton.monster`)"));
                Assert.That(routes, Does.Contain("Host(`second-static-site.apps.automaton.monster`)"));
                Assert.That(routes, Does.Contain($"/runtime-tunnel/apps/{first.WebAppId}/targets/{first.Id}/$1"));
                Assert.That(routes, Does.Contain($"/runtime-tunnel/apps/{second.WebAppId}/targets/{second.Id}/$1"));
                Assert.That(routes, Does.Contain($"slug-{first.WebAppId:N}-auth:"));
                Assert.That(routes, Does.Contain($"slug-{second.WebAppId:N}-auth:"));
                Assert.That(routes, Does.Contain("forwardAuth:"));
                Assert.That(db.HostedRunners, Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AppDeploymentTarget AddSite(ApplicationDbContext db, Guid runnerId, string slug)
    {
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        db.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = slug,
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            IsPublished = true,
            PublicUrl = slug,
            RuntimeVisibility = AppRuntimeVisibilities.Public,
            AccessStatus = AppAccessStatuses.Active,
            RequiresAuthentication = true,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { target }
        });
        return target;
    }

    private static BackendRouteConfigService CreateRouteService(ApplicationDbContext db, string root)
        => new(db,
            Options.Create(new PublishedAppsConfig
            {
                PublicBaseUrl = "https://apps.automaton.monster",
                TraefikDynamicConfigRoot = root,
                ApiBaseUrl = "https://api.test",
                RuntimeProxyAuthKey = "test-runtime-key"
            }),
            new StubWebHostEnvironment(),
            NullLogger<BackendRouteConfigService>.Instance);

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
