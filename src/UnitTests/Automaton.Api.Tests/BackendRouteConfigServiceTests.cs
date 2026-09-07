using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Models;
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
        defaultTarget.GenericRuntimeDeployed = true;
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Managed App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            ApplicationContractJson = GenericRuntimeContractJson(),
            RequiresAuthentication = true,
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
            Assert.That(content, Does.Contain($"Host(`app-{appId:N}.localhost`) && PathPrefix(`/`)\"{Environment.NewLine}      priority: 50"));
            Assert.That(content, Does.Contain("tls: {}"));
            Assert.That(content, Does.Not.Contain("certResolver"));
            Assert.That(content, Does.Contain(
                $"/apps/{appId}/targets/{defaultTarget.Id}/$1"));
            Assert.That(content, Does.Not.Contain("-api:"));
            Assert.That(content, Does.Not.Contain("-frontend"));
            Assert.That(content, Does.Contain("http://runner-a:8080"));
            Assert.That(content, Does.Contain($"private-{appId:N}-auth-headers"));
            Assert.That(content, Does.Contain("forwardAuth:"));
            Assert.That(content, Does.Contain($"private-{appId:N}-auth-strip"));
            Assert.That(content, Does.Contain("https://api.test/internal/runtime/auth"));
            Assert.That(content, Does.Contain($"X-App-Id: \"{appId}\""));
            Assert.That(content, Does.Contain("X-App-Id: \"\""));
            Assert.That(content, Does.Contain("X-Automaton-Runtime-Auth: \"\""));
            Assert.That(content, Does.Contain(
                $"middlewares: [\"private-{appId:N}-auth-headers\", \"private-{appId:N}-auth\", \"private-{appId:N}-auth-strip\", \"private-{appId:N}-rewrite\"]"));
        });
    }

    [Test]
    public async Task RefreshAsync_DoesNotAuthorizeUnrestrictedRoute()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        AddHostedRunner(dbContext, runnerId);
        AddApp(dbContext, appId, target, GenericRuntimeContractJson());
        dbContext.WebApps.Local.Single().RequiresAuthentication = false;
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();
        var content = await File.ReadAllTextAsync(Path.Combine(rootPath, "managed-runtime-routes.yml"));

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain($"private-{appId:N}-runtime"));
            Assert.That(content, Does.Contain($"private-{appId:N}-rewrite"));
            Assert.That(content, Does.Not.Contain($"private-{appId:N}-auth"));
            Assert.That(content, Does.Not.Contain("forwardAuth:"));
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
    public async Task RefreshAsync_UsesTargetLocalPromotionWhenAnotherTargetChangesAppContract()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var targetA = AppDeploymentTarget.CreateDefault(appId, runnerId);
        targetA.GenericRuntimeDeployed = true;
        var targetB = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = "Preview",
            Slug = "preview",
            ExecutionRunnerId = runnerId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        targetB.SynchronizeDefaultConstraint();
        AddHostedRunner(dbContext, runnerId);
        AddApp(dbContext, appId, targetA, GenericRuntimeContractJson());
        var app = dbContext.WebApps.Local.Single();
        app.DeploymentTargets.Add(targetB);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, rootPath);
        await service.RefreshAsync();
        var routePath = Path.Combine(rootPath, "managed-runtime-routes.yml");
        var routeBeforeTargetBDeployment = await File.ReadAllTextAsync(routePath);

        app.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract());
        targetB.GenericRuntimeDeployed = false;
        await dbContext.SaveChangesAsync();
        await service.RefreshAsync();

        Assert.That(await File.ReadAllTextAsync(routePath), Is.EqualTo(routeBeforeTargetBDeployment));
    }

    [Test]
    public async Task RefreshAsync_WritesIndependentRoute_ForNamedTarget()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var defaultTarget = AppDeploymentTarget.CreateDefault(appId, runnerId);
        defaultTarget.GenericRuntimeDeployed = true;
        var namedTarget = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = "Server EU",
            Slug = "server-eu",
            ExecutionRunnerId = runnerId,
            GenericRuntimeDeployed = true,
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
            ApplicationContractJson = GenericRuntimeContractJson(),
            RequiresAuthentication = true,
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
                $"middlewares: [\"target-{namedTarget.Id:N}-auth-headers\", \"target-{namedTarget.Id:N}-auth\", \"target-{namedTarget.Id:N}-auth-strip\", \"target-{namedTarget.Id:N}-rewrite\"]"));
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
        target.GenericRuntimeDeployed = true;
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
            ApplicationContractJson = GenericRuntimeContractJson(),
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { target }
        });
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();
        var content = await File.ReadAllTextAsync(Path.Combine(rootPath, "managed-runtime-routes.yml"));

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain("service: \"api-live-edge\""));
            Assert.That(content, Does.Contain($"Host(`app-{appId:N}.localhost`) && PathPrefix(`/`)\"{Environment.NewLine}      priority: 50"));
            Assert.That(content, Does.Contain(
                $"/runtime-tunnel/apps/{appId}/targets/{target.Id}/$1"));
            Assert.That(content, Does.Not.Contain("-api:"));
            Assert.That(content, Does.Not.Contain("-frontend"));
            Assert.That(content, Does.Not.Contain("http://runner-"));
        });
    }

    [Test]
    public async Task RefreshAsync_UsesStableLiveRuntimeTunnelServiceAcrossApiColorChanges()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
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
                Name = "Color-aware app",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                ApplicationContractJson = GenericRuntimeContractJson(),
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow,
                DeploymentTargets = { target }
            });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, rootPath);

        await service.RefreshAsync();
        var routesPath = Path.Combine(rootPath, "managed-runtime-routes.yml");
        var firstContent = await File.ReadAllTextAsync(routesPath);
            Assert.That(firstContent, Does.Contain("service: \"api-live-edge\""));

        await service.RefreshAsync();
        var secondContent = await File.ReadAllTextAsync(routesPath);
            Assert.That(secondContent, Does.Contain("service: \"api-live-edge\""));
            Assert.That(secondContent, Does.Not.Contain("api-blue-edge"));
            Assert.That(secondContent, Does.Not.Contain("api-green-edge"));
    }

    [Test]
    public async Task RefreshAsync_DoesNotRouteInfrastructureRunnerThroughRuntimeTunnel()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
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
            ApplicationContractJson = GenericRuntimeContractJson(),
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
        target.GenericRuntimeDeployed = true;
        dbContext.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "Hosted Runner",
            Status = "Running", ContainerName = "runner-a", RunnerId = runnerId,
            RuntimeEndpoint = "http://runner-a:8080"
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId, Name = "Private preview", Summary = string.Empty, Description = string.Empty,
            Definition = "{}", ApplicationContractJson = GenericRuntimeContractJson(),
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
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

    [Test]
    public async Task RefreshAsync_DoesNotRouteLegacyControllerOnlyDeployment()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        AddHostedRunner(dbContext, runnerId);
        AddApp(dbContext, appId, target);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();

        Assert.That(File.Exists(Path.Combine(rootPath, "managed-runtime-routes.yml")), Is.False);
    }

    [Test]
    public async Task RefreshAsync_DoesNotRouteTargetWithoutGenericPromotionMarker()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        AddHostedRunner(dbContext, runnerId);
        AddApp(dbContext, appId, target);
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();

        Assert.That(File.Exists(Path.Combine(rootPath, "managed-runtime-routes.yml")), Is.False);
    }

    [Test]
    public async Task RefreshAsync_RoutesSuccessfullyPromotedGenericRuntime()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        AddHostedRunner(dbContext, runnerId);
        AddApp(dbContext, appId, target, GenericRuntimeContractJson());
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext, rootPath).RefreshAsync();

        Assert.That(File.Exists(Path.Combine(rootPath, "managed-runtime-routes.yml")), Is.True);
    }

    [Test]
    public async Task RefreshAsync_FailedReplacementPreservesPreviouslyHealthyRoute()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        AddHostedRunner(dbContext, runnerId);
        AddApp(dbContext, appId, target, GenericRuntimeContractJson());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, rootPath);

        await service.RefreshAsync();
        var routePath = Path.Combine(rootPath, "managed-runtime-routes.yml");
        var healthyRoute = await File.ReadAllTextAsync(routePath);

        var stateService = new AppDeploymentTargetStateService(dbContext);
        await stateService.MarkStartedAsync(appId, target.Id);
        await stateService.MarkCompletedAsync(
            appId,
            target.Id,
            success: false,
            genericRuntimeDeployed: false);
        await service.RefreshAsync();

        Assert.That(await File.ReadAllTextAsync(routePath), Is.EqualTo(healthyRoute));
    }

    [Test]
    public async Task RefreshAsync_LegacyCompletionAfterRunnerReassignmentDoesNotResurrectRoute()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();
        var appId = Guid.NewGuid();
        var oldRunnerId = Guid.NewGuid();
        var newRunnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, oldRunnerId);
        target.GenericRuntimeDeployed = true;
        AddHostedRunner(dbContext, oldRunnerId);
        AddHostedRunner(dbContext, newRunnerId);
        AddApp(dbContext, appId, target, GenericRuntimeContractJson());
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, rootPath);

        await service.RefreshAsync();
        var routePath = Path.Combine(rootPath, "managed-runtime-routes.yml");
        Assert.That(File.Exists(routePath), Is.True);

        target.ExecutionRunnerId = newRunnerId;
        target.GenericRuntimeDeployed = false;
        await dbContext.SaveChangesAsync();

        var stateService = new AppDeploymentTargetStateService(dbContext);
        await stateService.MarkStartedAsync(appId, target.Id);
        await stateService.MarkCompletedAsync(
            appId,
            target.Id,
            success: true,
            genericRuntimeDeployed: false);
        await service.RefreshAsync();

        Assert.That(File.Exists(routePath), Is.False);
        Assert.That(target.GenericRuntimeDeployed, Is.False);
    }

    private static void AddHostedRunner(ApplicationDbContext dbContext, Guid runnerId)
    {
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
    }

    private static void AddApp(
        ApplicationDbContext dbContext,
        Guid appId,
        AppDeploymentTarget target,
        string? applicationContractJson = null)
    {
        applicationContractJson ??= ApplicationContractSerialization.Serialize(new ApplicationContract());
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Generic runtime app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            ApplicationContractJson = applicationContractJson,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { target }
        });
    }

    private static BackendRouteConfigService CreateService(ApplicationDbContext dbContext, string rootPath)
    {
        return new BackendRouteConfigService(
            dbContext,
            Options.Create(new PublishedAppsConfig
            {
                TraefikDynamicConfigRoot = rootPath,
                ApiBaseUrl = "https://api.test",
                RuntimeProxyAuthKey = "test-runtime-key"
            }),
            new StubWebHostEnvironment(),
            NullLogger<BackendRouteConfigService>.Instance);
    }

    private static string GenericRuntimeContractJson() =>
        ApplicationContractSerialization.Serialize(new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "test/runtime:1",
                Command = new ApplicationCommandSpec { Executable = "runtime-server" },
                Port = 8080
            }
        });

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
