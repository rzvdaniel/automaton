using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
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
            SubscriptionPlanId = Guid.NewGuid(),
            Name = "Hosted Runner",
            Status = "Running",
            ContainerName = "runner-a",
            RunnerId = runnerId,
            RuntimeEndpoint = "http://runner-a:8080"
        });

        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Managed App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            ExecutionRunnerId = runnerId
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, rootPath);
        await service.RefreshAsync();

        var outputPath = Path.Combine(rootPath, "managed-runtime-routes.yml");
        var content = await File.ReadAllTextAsync(outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain($"Host(`app-{appId:N}.localhost`) && PathPrefix(`/api`)"));
            Assert.That(content, Does.Contain($"/apps/{appId}/api/$1"));
            Assert.That(content, Does.Contain("http://runner-a:8080"));
        });
    }

    [Test]
    public async Task RefreshAsync_WritesEmptyConfig_WhenNoRuntimeEndpointExists()
    {
        var rootPath = CreateTempDirectory();
        await using var dbContext = CreateDbContext();

        dbContext.WebApps.Add(new WebApp
        {
            Id = Guid.NewGuid(),
            Name = "Unrouted App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            ExecutionRunnerId = Guid.NewGuid()
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, rootPath);
        await service.RefreshAsync();

        var outputPath = Path.Combine(rootPath, "managed-runtime-routes.yml");
        var content = await File.ReadAllTextAsync(outputPath);

        Assert.Multiple(() =>
        {
            Assert.That(content, Does.Contain("routers: {}"));
            Assert.That(content, Does.Contain("middlewares: {}"));
            Assert.That(content, Does.Contain("services: {}"));
        });
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
