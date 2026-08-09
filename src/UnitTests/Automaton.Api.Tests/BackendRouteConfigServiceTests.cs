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
public class BackendRouteConfigServiceTests
{
    [Test]
    public async Task RefreshAsync_WritesRoute_ForAppWithRuntimeEndpoint()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();

        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        dbContext.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "Hosted Runner",
            Status = "Running",
            ContainerName = "runner-a",
            RunnerId = runnerId,
            RuntimeEndpoint = "http://runner-a:8080"
        });

        var defaultTarget = AppDeploymentTarget.CreateDefault(appId, runnerId);
        defaultTarget.FrontendDeployed = true;
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Managed App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { defaultTarget }
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, rootPath);
        await service.RefreshAsync();

        var outputPath = Path.Combine(rootPath, "managed-runtime-routes.yml");
        var content = await File.ReadAllTextAsync(outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain($"Host(`app-{appId:N}.localhost`) && PathPrefix(`/api`)"));
            Assert.That(content, Does.Contain($"Host(`app-{appId:N}.localhost`) && PathPrefix(`/`)\"\n      priority: 50"));
            Assert.That(content, Does.Contain("tls: {}"));
            Assert.That(content, Does.Not.Contain("certResolver"));
            Assert.That(content, Does.Contain(
                $"/apps/{appId}/targets/{defaultTarget.Id}/api/$1"));
            Assert.That(content, Does.Contain("http://runner-a:8080"));
        });
    }

    [Test]
    public async Task RefreshAsync_WritesEmptyConfig_WhenNoRuntimeEndpointExists()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();

        var appId = Guid.NewGuid();
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Unrouted App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { AppDeploymentTarget.CreateDefault(appId, Guid.NewGuid()) }
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, rootPath);
        await service.RefreshAsync();

        Assert.That(File.Exists(Path.Combine(rootPath, "managed-runtime-routes.yml")), Is.False);
    }

    [Test]
    public async Task RefreshAsync_WritesIndependentRoute_ForNamedTarget()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var defaultTarget = AppDeploymentTarget.CreateDefault(appId, runnerId);
        defaultTarget.FrontendDeployed = true;
        var namedTarget = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = "Server EU",
            Slug = "server-eu",
            ExecutionRunnerId = runnerId,
            FrontendDeployed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        namedTarget.SynchronizeDefaultConstraint();
        dbContext.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "Hosted Runner",
            Status = "Running",
            ContainerName = "runner-a",
            RunnerId = runnerId,
            RuntimeEndpoint = "http://runner-a:8080"
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Multi-target App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { defaultTarget, namedTarget }
        });
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();
        var content = await File.ReadAllTextAsync(
            Path.Combine(rootPath, "managed-runtime-routes.yml"));
        var targetPrefix = $"/a/{appId}/t/{namedTarget.Id}";

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain($"PathPrefix(`{targetPrefix}`)"));
            Assert.That(content, Does.Contain(
                $"/apps/{appId}/targets/{namedTarget.Id}/$1"));
            Assert.That(content, Does.Contain(
                $"/apps/{appId}/targets/{defaultTarget.Id}/$1"));
        });

        dbContext.AppDeploymentTargets.Remove(namedTarget);
        await dbContext.SaveChangesAsync();
        await CreateService(dbContext, rootPath).RefreshAsync();
        content = await File.ReadAllTextAsync(
            Path.Combine(rootPath, "managed-runtime-routes.yml"));

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Not.Contain(targetPrefix));
            Assert.That(content, Does.Contain(
                $"/apps/{appId}/targets/{defaultTarget.Id}/$1"));
        });
    }

    [Test]
    public async Task RefreshAsync_RoutesSelfHostedRunnerThroughApiRuntimeTunnel()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.FrontendDeployed = true;
        dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "Privileged host",
            Status = "connected",
            Purpose = "Managed",
            ApplicationProfile = "PrivilegedHost"
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Self-hosted app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { target }
        });
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();
        var content = await File.ReadAllTextAsync(Path.Combine(rootPath, "managed-runtime-routes.yml"));

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain("service: \"api-blue-edge\""));
            Assert.That(content, Does.Contain($"Host(`app-{appId:N}.localhost`) && PathPrefix(`/`)\"\n      priority: 50"));
            Assert.That(content, Does.Contain(
                $"/runtime-tunnel/apps/{appId}/targets/{target.Id}/api/$1"));
            Assert.That(content, Does.Contain(
                $"/runtime-tunnel/apps/{appId}/targets/{target.Id}/$1"));
            Assert.That(content, Does.Not.Contain("http://runner-"));
        });
    }

    [Test]
    public async Task RefreshAsync_DoesNotRouteInfrastructureRunnerThroughRuntimeTunnel()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.FrontendDeployed = true;
        dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "Infrastructure runner",
            Status = "connected",
            Purpose = "Infrastructure"
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Infrastructure target",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { target }
        });
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();

        Assert.That(File.Exists(Path.Combine(rootPath, "managed-runtime-routes.yml")), Is.False);
    }

    [Test]
    public async Task RefreshAsync_DoesNotWritePublicSlugRoute_ForPrivateMediaPreview()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.FrontendDeployed = true;
        dbContext.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "Hosted Runner",
            Status = "Running", ContainerName = "runner-a", RunnerId = runnerId,
            RuntimeEndpoint = "http://runner-a:8080"
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId, Name = "Private preview", Summary = string.Empty, Description = string.Empty,
            Definition = "{}", Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
            IsPublished = true, PublicUrl = "private-preview",
            RuntimeVisibility = AppRuntimeVisibilities.Private,
            CatalogVisibility = AppCatalogVisibilities.Listed,
            MarketplacePreviewMode = MarketplacePreviewModes.MediaOnly,
            DeploymentTargets = { target }
        });
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();
        var content = await File.ReadAllTextAsync(Path.Combine(rootPath, "managed-runtime-routes.yml"));

        Assert.That(content, Does.Not.Contain("private-preview"));
    }

    private static BackendRouteConfigService CreateService(ApplicationDbContext dbContext, string rootPath)
    {
        return new BackendRouteConfigService(
            dbContext,
            Options.Create(new PublishedAppsConfig
            {
                TraefikDynamicConfigRoot = rootPath
            }),
            new StubWebHostEnvironment(),
            NullLogger<BackendRouteConfigService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
