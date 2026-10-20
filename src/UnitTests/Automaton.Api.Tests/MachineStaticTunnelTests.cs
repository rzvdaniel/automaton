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
public sealed class MachineStaticTunnelTests
{
    private const string PublicSlug = "static-site-one";

    [Test]
    public async Task ForwardAsync_UsesStaticSiteDestinationAndRejectsRevokedPublicAccess()
    {
        await using var fixture = await StaticSiteFixture.CreateAsync();
        var response = await fixture.ForwardAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(fixture.LastRequest?.Destination, Is.EqualTo(TunnelDestinations.StaticSite));
            Assert.That(fixture.LastRequest?.RuntimePath, Is.EqualTo("assets/site.css"));
        });

        var dispatchedRequest = fixture.LastRequest;
        await fixture.RevokeAccessAsync();
        response = await fixture.ForwardAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(fixture.LastRequest, Is.SameAs(dispatchedRequest));
        });
    }

    private sealed class StaticSiteFixture : IAsyncDisposable
    {
        private const string PublicHost = "static-site-one.apps.automaton.monster";
        private readonly ApplicationDbContext db;
        private readonly RuntimeTunnelService service;
        private readonly Guid appId;
        private readonly Guid targetId;

        private StaticSiteFixture(ApplicationDbContext db, RuntimeTunnelService service,
            Guid appId, Guid targetId, CapturingHubContext hub)
        {
            this.db = db;
            this.service = service;
            this.appId = appId;
            this.targetId = targetId;
            Hub = hub;
        }

        public CapturingHubContext Hub { get; }
        public TunnelRequest? LastRequest => Hub.LastRequest;

        public static async Task<StaticSiteFixture> CreateAsync()
        {
            var db = CreateDbContext();
            var identity = new ApiInstanceIdentityService();
            var appId = Guid.NewGuid();
            var targetId = await AddPublishedSiteAsync(db, appId, identity);
            var (service, hub) = CreateRuntimeService(db, identity);
            return new StaticSiteFixture(db, service, appId, targetId, hub);
        }

        public Task<TunnelResponse> ForwardAsync()
        {
            var request = new DefaultHttpContext().Request;
            request.Host = new HostString(PublicHost);
            return service.ForwardAsync(appId, targetId, request, "assets/site.css", CancellationToken.None);
        }

        public async Task RevokeAccessAsync()
        {
            db.WebApps.Local.Single(app => app.Id == appId).AccessStatus = AppAccessStatuses.Disabled;
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private static async Task<Guid> AddPublishedSiteAsync(
        ApplicationDbContext db, Guid appId, ApiInstanceIdentityService identity)
    {
        var runnerId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        db.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "Machine management",
            Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            ConnectionId = "machine-runner-connection",
            ApiInstanceId = identity.ApiInstanceId,
            LastHeartbeatAtUtc = DateTime.UtcNow
        });
        db.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Published static site",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            IsPublished = true,
            PublicUrl = PublicSlug,
            RuntimeVisibility = AppRuntimeVisibilities.Public,
            AccessStatus = AppAccessStatuses.Active,
            RequiresAuthentication = true,
            DeploymentTargets = { target }
        });
        await db.SaveChangesAsync();
        return target.Id;
    }

    private static ApplicationDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static (RuntimeTunnelService Service, CapturingHubContext Hub) CreateRuntimeService(
        ApplicationDbContext db, ApiInstanceIdentityService identity)
    {
        var registry = new TunnelRequestRegistry();
        var hub = new CapturingHubContext(registry);
        var sender = new RuntimeTunnelRequestSender(hub, registry,
            Options.Create(new RuntimeTunnelConfig()), NullLogger<RuntimeTunnelRequestSender>.Instance);
        var service = new RuntimeTunnelService(db, sender,
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" }),
            identity, NullLogger<RuntimeTunnelService>.Instance);
        return (service, hub);
    }

    private sealed class CapturingHubContext : IHubContext<AutomatonHub>
    {
        private readonly CapturingHubClients clients;

        public CapturingHubContext(TunnelRequestRegistry registry)
        {
            clients = new CapturingHubClients(registry, request => LastRequest = request);
        }

        public IHubClients Clients => clients;
        public IGroupManager Groups { get; } = null!;
        public TunnelRequest? LastRequest { get; private set; }
    }

    private sealed class CapturingHubClients(TunnelRequestRegistry registry, Action<TunnelRequest> capture) : IHubClients
    {
        private readonly CapturingClientProxy client = new(registry, capture);
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

    private sealed class CapturingClientProxy(TunnelRequestRegistry registry, Action<TunnelRequest> capture)
        : ISingleClientProxy
    {
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == AutomatonHubMethods.TunnelRequest && args[0] is TunnelRequest request)
            {
                capture(request);
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
