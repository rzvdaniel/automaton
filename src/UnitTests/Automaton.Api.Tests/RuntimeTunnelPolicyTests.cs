using Automaton.Core.Constants;
using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class RuntimeTunnelPolicyTests
{
    [TestCase("runner-deployment-diagnostic.apps.automaton.monster", true, "runner-deployment-diagnostic")]
    [TestCase("app-builder.apps.automaton.monster", true, "app-builder")]
    [TestCase("app-162a91472be14b93bd6a607c2afe5107.apps.automaton.monster", false, "")]
    [TestCase("foo.apps.automaton.monster.evil.example", false, "")]
    [TestCase("foo.bar.apps.automaton.monster", false, "")]
    [TestCase("apps.automaton.monster", false, "")]
    public void PublishedAppHostNames_RecognizesOnlyExactSingleLabelHosts(string host, bool expected, string expectedSlug)
    {
        var result = PublishedAppHostNames.TryGetPublishedAppSlug(
            host, "apps.automaton.monster", out var slug);

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(slug, Is.EqualTo(expectedSlug));
    }

    [Test]
    public void PublishedAppHostNames_EnforcesDnsLabelBoundariesAndNormalization()
    {
        var validSlug = new string('a', 63);
        var invalidSlug = new string('a', 64);

        Assert.Multiple(() =>
        {
            Assert.That(PublishedAppHostNames.IsValidPublishedSlug(validSlug), Is.True);
            Assert.That(PublishedAppHostNames.IsValidPublishedSlug(invalidSlug), Is.False);
            Assert.That(PublishedAppHostNames.TryGetPublishedAppSlug(
                "APP-BUILDER.apps.automaton.monster", "apps.automaton.monster", out var normalized), Is.True);
            Assert.That(normalized, Is.EqualTo("app-builder"));
            Assert.That(PublishedAppHostNames.IsValidPublishedSlug(
                "app-162a91472be14b93bd6a607c2afe5107"), Is.False);
        });
    }

    [TestCase("Managed", "Standard", false)]
    [TestCase("Managed", "PrivilegedHost", false)]
    [TestCase("Managed", "MachineManagement", true)]
    [TestCase("Managed", "Unknown", false)]
    public void AllowsRuntimeTunnel_OnlyAllowsApplicationProfiles(string purpose, string? profile, bool expected)
    {
        Assert.That(SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(purpose, profile), Is.EqualTo(expected));
    }

    [Test]
    public void ApplyTunnelHeaders_GivesTheAppThePublicHostAndSchemeAndIgnoresClientValues()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var message = new HttpRequestMessage(HttpMethod.Get, "http://runner.tunnel/");
        message.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example");
        message.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        message.Headers.TryAddWithoutValidation(TunnelHeaders.Destination, TunnelDestinations.FastApi);
        message.Headers.TryAddWithoutValidation("Origin", "https://app-1.apps.example.test");

        RuntimeTunnelRequestSender.ApplyTunnelHeaders(
            message.Headers, appId, targetId, staticSite: false, "app-1.apps.example.test", "https");

        Assert.Multiple(() =>
        {
            Assert.That(message.Headers.GetValues("X-Forwarded-Host"), Is.EqualTo(new[] { "app-1.apps.example.test" }));
            Assert.That(message.Headers.GetValues("X-Forwarded-Proto"), Is.EqualTo(new[] { "https" }));
            Assert.That(message.Headers.GetValues("Origin"), Is.EqualTo(new[] { "https://app-1.apps.example.test" }));
            Assert.That(message.Headers.GetValues(TunnelHeaders.Destination), Is.EqualTo(new[] { TunnelDestinations.RuntimeGateway }));
            Assert.That(message.Headers.GetValues(TunnelHeaders.AppId), Is.EqualTo(new[] { appId.ToString("D") }));
            Assert.That(message.Headers.GetValues(TunnelHeaders.TargetId), Is.EqualTo(new[] { targetId.ToString("D") }));
        });
    }

    [TestCase("assets/site.css", "?v=1", "http://runner.tunnel/assets/site.css?v=1")]
    [TestCase("a b/ü.txt", "", "http://runner.tunnel/a%20b/%C3%BC.txt")]
    [TestCase("", "", "http://runner.tunnel/")]
    public void BuildUri_EncodesThePathForTheRunner(string path, string query, string expected)
        => Assert.That(RuntimeTunnelRequestSender.BuildUri(path, query).AbsoluteUri, Is.EqualTo(expected));

    [Test]
    public void IsExpectedAppHost_RejectsDirectApiHost()
    {
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId);
        target.WebApp = new WebApp
        {
            Id = appId,
            Name = "Private app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            RuntimeVisibility = AppRuntimeVisibilities.Private
        };
        var config = new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" };

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeTunnelService.IsExpectedAppHost(
                $"app-{appId:N}.apps.automaton.monster", target, config), Is.True);
            Assert.That(RuntimeTunnelService.IsExpectedAppHost("api.automaton.monster", target, config), Is.False);
        });
    }

    [Test]
    public void IsExpectedAppHost_AcceptsPublicHostForLiveSelfHostedTarget()
    {
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId);
        target.WebApp = new WebApp
        {
            Id = appId,
            Name = "Published app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            IsPublished = true,
            RuntimeVisibility = AppRuntimeVisibilities.Public,
            AccessStatus = AppAccessStatuses.Active,
            PublicUrl = "runner-deployment-diagnostic-20260804"
        };
        var config = new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" };

        Assert.That(RuntimeTunnelService.IsExpectedAppHost(
            "runner-deployment-diagnostic-20260804.apps.automaton.monster", target, config), Is.True);

        target.IsDefault = false;
        target.SynchronizeDefaultConstraint();
        Assert.That(RuntimeTunnelService.IsExpectedAppHost(
            "runner-deployment-diagnostic-20260804.apps.automaton.monster", target, config), Is.False);
    }

    [Test]
    public void IsExpectedAppHost_AcceptsVerifiedCustomDomainOfPublicApp()
    {
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId);
        target.WebApp = new WebApp
        {
            Id = appId,
            Name = "Blog",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            IsPublished = true,
            RuntimeVisibility = AppRuntimeVisibilities.Public,
            AccessStatus = AppAccessStatuses.Active,
            PublicUrl = "blog"
        };
        var config = new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" };
        string[] verified = ["blog.example.com"];

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeTunnelService.IsExpectedAppHost("Blog.Example.com", target, config, verified), Is.True);
            Assert.That(RuntimeTunnelService.IsExpectedAppHost("other.example.com", target, config, verified), Is.False);
            Assert.That(RuntimeTunnelService.IsExpectedAppHost("blog.example.com", target, config), Is.False);
        });

        target.WebApp.RuntimeVisibility = AppRuntimeVisibilities.Private;
        Assert.That(RuntimeTunnelService.IsExpectedAppHost("blog.example.com", target, config, verified), Is.False);
    }


    [Test]
    public void RunnerPresenceEvaluator_RejectsOfflineRunner()
    {
        Assert.That(RunnerPresenceEvaluator.IsOnline(
            "stale-connection",
            DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold - TimeSpan.FromSeconds(1),
            DateTime.UtcNow), Is.False);
    }

    [TestCase("api-blue", "api-blue", true)]
    [TestCase("api-green", "api-blue", false)]
    [TestCase(null, "api-blue", false)]
    public void RuntimeTunnel_RequiresRunnerOnCurrentApiInstance(
        string? runnerApiInstanceId,
        string currentApiInstanceId,
        bool expected)
    {
        Assert.That(
            RuntimeTunnelService.IsCurrentApiInstance(runnerApiInstanceId, currentApiInstanceId),
            Is.EqualTo(expected));
    }

    [Test]
    public async Task ResolveAsync_AllowsGenericRuntimeDeploymentWithoutLegacyMarker()
    {
        var appId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var (target, identity) = await SeedGenericRuntimeDeploymentAsync(dbContext, appId);
        var service = CreateRuntimeTunnelService(dbContext, identity);
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString($"app-{appId:N}.apps.automaton.monster");

        var resolved = await service.ResolveAsync(appId, target.Id, request, string.Empty, CancellationToken.None);

        Assert.That(resolved.Failure, Is.Null);
        Assert.That(resolved.Runner, Is.Not.Null);
    }

    [Test]
    public async Task ResolveAsync_AnswersUpgradeNeededForARunnerThatPredatesTheHttp2Tunnel()
    {
        var appId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var (target, identity) = await SeedGenericRuntimeDeploymentAsync(dbContext, appId);
        dbContext.Runners.Single().ApiCompatibilityVersion = RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands;
        await dbContext.SaveChangesAsync();
        var service = CreateRuntimeTunnelService(dbContext, identity);
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString($"app-{appId:N}.apps.automaton.monster");

        var resolved = await service.ResolveAsync(appId, target.Id, request, string.Empty, CancellationToken.None);

        Assert.That(resolved.Failure?.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(resolved.Failure?.Body, Does.Contain("upgraded"));
    }

    [Test]
    public async Task ResolveAsync_AnswersNotConnectedWhenTheRunnerHasNoTunnelConnection()
    {
        var appId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var (target, identity) = await SeedGenericRuntimeDeploymentAsync(dbContext, appId);
        var service = new RuntimeTunnelService(
            dbContext, new RunnerTunnelRegistry(),
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" }),
            identity, NullLogger<RuntimeTunnelService>.Instance, new RuntimeTunnelTargetCache(TimeSpan.Zero, TimeProvider.System));
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString($"app-{appId:N}.apps.automaton.monster");

        var resolved = await service.ResolveAsync(appId, target.Id, request, string.Empty, CancellationToken.None);

        Assert.That(resolved.Failure?.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(resolved.Failure?.Body, Does.Contain("not connected"));
    }

    private static async Task<(AppDeploymentTarget Target, ApiInstanceIdentityService Identity)> SeedGenericRuntimeDeploymentAsync(
        ApplicationDbContext dbContext,
        Guid appId)
    {
        var runnerId = Guid.NewGuid();
        var identity = new ApiInstanceIdentityService();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "Machine Management",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            ConnectionId = "connection",
            ApiInstanceId = identity.ApiInstanceId,
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.Current,
            LastHeartbeatAtUtc = DateTime.UtcNow
        });
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Generic runtime app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { target }
        });
        await dbContext.SaveChangesAsync();
        return (target, identity);
    }

    private static RuntimeTunnelService CreateRuntimeTunnelService(
        ApplicationDbContext dbContext,
        ApiInstanceIdentityService identity)
    {
        var tunnels = new RunnerTunnelRegistry();
        foreach (var runner in dbContext.Runners.AsNoTracking().ToList())
            tunnels.Add(runner.Id, new TunnelConnectionStream(new MemoryStream()), out _);
        return new RuntimeTunnelService(
            dbContext,
            tunnels,
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" }),
            identity,
            NullLogger<RuntimeTunnelService>.Instance,
            new RuntimeTunnelTargetCache(TimeSpan.Zero, TimeProvider.System));
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
