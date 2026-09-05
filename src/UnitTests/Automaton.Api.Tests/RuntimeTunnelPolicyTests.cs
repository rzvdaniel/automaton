using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
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

    [TestCase("Managed", "Standard", true)]
    [TestCase("Managed", "PrivilegedHost", true)]
    [TestCase("Infrastructure", null, false)]
    [TestCase("Managed", "Unknown", false)]
    public void AllowsRuntimeTunnel_OnlyAllowsApplicationProfiles(string purpose, string? profile, bool expected)
    {
        Assert.That(SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(purpose, profile), Is.EqualTo(expected));
    }

    [Test]
    public async Task ReadBodyAsync_ReadsUnknownLengthBodyWithinLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("chunked-body"));
        context.Request.ContentLength = null;

        var result = await RuntimeTunnelService.ReadBodyAsync(context.Request, 64, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TooLarge, Is.False);
            Assert.That(Encoding.UTF8.GetString(result.Body!), Is.EqualTo("chunked-body"));
        });
    }

    [Test]
    public async Task ReadBodyAsync_ReadsKnownLengthBodyWithinLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("known-body"));
        context.Request.ContentLength = 10;

        var result = await RuntimeTunnelService.ReadBodyAsync(context.Request, 64, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TooLarge, Is.False);
            Assert.That(Encoding.UTF8.GetString(result.Body!), Is.EqualTo("known-body"));
        });
    }

    [Test]
    public async Task ReadBodyAsync_RejectsUnknownLengthBodyAboveLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("too-large"));
        context.Request.ContentLength = null;

        var result = await RuntimeTunnelService.ReadBodyAsync(context.Request, 3, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TooLarge, Is.True);
            Assert.That(result.Body, Is.Null);
        });
    }

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
    public async Task ForwardAsync_AllowsGenericRuntimeDeploymentWithoutLegacyMarker()
    {
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        target.LegacyControllerRuntimeDeployed = false;

        await using var dbContext = CreateDbContext();
        var identity = new ApiInstanceIdentityService();
        dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "Self-hosted standard",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.Standard,
            ConnectionId = "connection",
            ApiInstanceId = identity.ApiInstanceId,
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

        var registry = new TunnelRequestRegistry();
        var service = new RuntimeTunnelService(
            dbContext,
            new CompletingHubContext(registry),
            registry,
            Options.Create(new RuntimeTunnelConfig()),
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" }),
            identity,
            NullLogger<RuntimeTunnelService>.Instance);
        var request = new DefaultHttpContext().Request;
        request.Host = new HostString($"app-{appId:N}.apps.automaton.monster");

        var response = await service.ForwardAsync(appId, target.Id, request, string.Empty, CancellationToken.None);

        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class CompletingHubContext(TunnelRequestRegistry registry) : IHubContext<AutomatonHub>
    {
        public IHubClients Clients { get; } = new CompletingHubClients(registry);
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class CompletingHubClients(TunnelRequestRegistry registry) : IHubClients
    {
        private readonly CompletingClientProxy client = new(registry);

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public ISingleClientProxy Client(string connectionId) => client;
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => client;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class CompletingClientProxy(TunnelRequestRegistry registry) : ISingleClientProxy
    {
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == AutomatonHubMethods.TunnelRequest && args[0] is TunnelRequest request)
            {
                registry.TryComplete(new TunnelResponse
                {
                    RequestId = request.RequestId,
                    StatusCode = StatusCodes.Status200OK
                });
            }

            return Task.CompletedTask;
        }
    }
}
