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

internal static class BackendRouteConfigTestFixture
{
    internal static void AddMachineRunner(ApplicationDbContext dbContext, Guid runnerId)
        => dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "Machine Management",
            Purpose = "Managed",
            ApplicationProfile = "MachineManagement"
        });

    internal static void AddApp(
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

    internal static BackendRouteConfigService CreateService(ApplicationDbContext dbContext, string rootPath, string? certificateResolver = null)
    {
        return new BackendRouteConfigService(
            dbContext,
            Options.Create(new PublishedAppsConfig
            {
                TraefikDynamicConfigRoot = rootPath,
                ApiBaseUrl = "https://api.test",
                RuntimeProxyAuthKey = "test-runtime-key",
                CertificateResolver = certificateResolver
            }),
            new StubWebHostEnvironment(),
            NullLogger<BackendRouteConfigService>.Instance);
    }

    internal static string GenericRuntimeContractJson() =>
        ApplicationContractSerialization.Serialize(new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec
            {
                Image = "test/runtime:1",
                Command = new ApplicationCommandSpec { Executable = "runtime-server" },
                Port = 8080
            }
        });

    internal static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    internal static string CreateTempDirectory()
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
