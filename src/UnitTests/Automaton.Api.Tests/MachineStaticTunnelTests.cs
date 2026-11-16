using Automaton.Core.Constants;
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
            Assert.That(response.Status, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(fixture.LastRequest?.StaticSite, Is.True);
        });

        var dispatchedRequest = fixture.LastRequest;
        await fixture.RevokeAccessAsync();
        response = await fixture.ForwardAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(fixture.LastRequest, Is.SameAs(dispatchedRequest));
        });
    }

    [Test]
    public async Task ForwardAsync_UsesStaticSiteDestinationOnUnpublishedPrivateHost()
    {
        await using var fixture = await StaticSiteFixture.CreateAsync();
        await fixture.UnpublishAsync();

        var response = await fixture.ForwardPrivateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(fixture.LastRequest?.StaticSite, Is.True);
        });

        var dispatchedRequest = fixture.LastRequest;
        response = await fixture.ForwardAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(fixture.LastRequest, Is.SameAs(dispatchedRequest));
        });
    }

    [Test]
    public async Task ForwardAsync_AnswersThePausedPageWithoutReachingTheRunner()
    {
        await using var fixture = await StaticSiteFixture.CreateAsync();
        await fixture.ForwardAsync();
        var dispatchedRequest = fixture.LastRequest;
        await fixture.PauseAsync();

        var response = await fixture.ForwardAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            Assert.That(response.Body, Does.Contain("This app is paused"));
            Assert.That(fixture.LastRequest, Is.SameAs(dispatchedRequest));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ForwardAsync_PreservesRuntimeGatewayForDynamicAndComposeApps(bool compose)
    {
        var contract = compose
            ? new ApplicationContract
            {
                Compose = new ApplicationComposeSpec
                {
                    PublicService = "web",
                    Services = [new ApplicationComposeService { Name = "web", Image = "nginx:alpine", Port = 8080 }]
                }
            }
            : new ApplicationContract { Runtime = { Command = new ApplicationCommandSpec { Executable = "serve" } } };
        await using var fixture = await StaticSiteFixture.CreateAsync(contract);

        var response = await fixture.ForwardAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(fixture.LastRequest?.StaticSite, Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ForwardAsync_DoesNotAdoptRetainedHostedAssignment(bool deleted)
    {
        await using var fixture = await StaticSiteFixture.CreateAsync();
        await fixture.MarkRetiredHostedAsync(deleted);

        var response = await fixture.ForwardAsync();

        Assert.That(response.Status, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(fixture.LastRequest, Is.Null);
    }

    private sealed class StaticSiteFixture : IAsyncDisposable
    {
        private const string PublicHost = "static-site-one.apps.automaton.monster";
        private readonly ApplicationDbContext db;
        private readonly RuntimeTunnelService service;
        private readonly Guid appId;
        private readonly Guid targetId;

        private StaticSiteFixture(ApplicationDbContext db, RuntimeTunnelService service,
            Guid appId, Guid targetId)
        {
            this.db = db;
            this.service = service;
            this.appId = appId;
            this.targetId = targetId;
        }

        public Dispatch? LastRequest { get; private set; }

        public async Task MarkRetiredHostedAsync(bool deleted)
        {
            db.HostedRunners.Add(new HostedRunner
            {
                Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "Retired Hosted",
                RunnerId = db.AppDeploymentTargets.Single(target => target.Id == targetId).ExecutionRunnerId,
                ContainerName = "retired-hosted", Status = "Running",
                DeletedAt = deleted ? DateTime.UtcNow : null
            });
            await db.SaveChangesAsync();
        }

        public static async Task<StaticSiteFixture> CreateAsync(ApplicationContract? contract = null)
        {
            var db = CreateDbContext();
            var identity = new ApiInstanceIdentityService();
            var appId = Guid.NewGuid();
            var targetId = await AddPublishedSiteAsync(db, appId, identity, contract ?? StaticContract());
            return new StaticSiteFixture(db, CreateRuntimeService(db, identity), appId, targetId);
        }

        private static ApplicationContract StaticContract() => new()
        {
            Build = new ApplicationBuildSpec
            {
                Command = new ApplicationCommandSpec { Executable = "build" },
                OutputDirectory = "dist"
            }
        };

        public Task<Outcome> ForwardAsync()
        {
            var request = new DefaultHttpContext().Request;
            request.Host = new HostString(PublicHost);
            return ResolveAsync(request);
        }

        public Task<Outcome> ForwardPrivateAsync()
        {
            var request = new DefaultHttpContext().Request;
            request.Host = new HostString(PublishedAppHostNames.BuildPrivateAppHost(appId, "apps.automaton.monster"));
            return ResolveAsync(request);
        }

        /// <summary>What the API would do with the request: answer with a failure itself, or send it to the Runner.</summary>
        private async Task<Outcome> ResolveAsync(HttpRequest request)
        {
            var resolved = await service.ResolveAsync(appId, targetId, request, "assets/site.css", CancellationToken.None);
            if (resolved.Failure is { } failure) return new Outcome(failure.StatusCode, failure.Body);
            LastRequest = new Dispatch(resolved.StaticSite);
            return new Outcome(StatusCodes.Status200OK, string.Empty);
        }

        public async Task UnpublishAsync()
        {
            var app = db.WebApps.Local.Single(item => item.Id == appId);
            app.IsPublished = false;
            app.RuntimeVisibility = AppRuntimeVisibilities.Private;
            await db.SaveChangesAsync();
        }

        public async Task PauseAsync()
        {
            db.WebApps.Local.Single(app => app.Id == appId).IsPaused = true;
            await db.SaveChangesAsync();
        }

        public async Task RevokeAccessAsync()
        {
            db.WebApps.Local.Single(app => app.Id == appId).AccessStatus = AppAccessStatuses.Disabled;
            await db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private static async Task<Guid> AddPublishedSiteAsync(
        ApplicationDbContext db, Guid appId, ApiInstanceIdentityService identity, ApplicationContract contract)
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
            ApiCompatibilityVersion = RunnerApiCompatibilityVersions.Current,
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
            ApplicationContractJson = ApplicationContractSerialization.Serialize(contract),
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

    private sealed record Outcome(int Status, string Body);

    private sealed record Dispatch(bool StaticSite);

    private static RuntimeTunnelService CreateRuntimeService(ApplicationDbContext db, ApiInstanceIdentityService identity)
    {
        var tunnels = new RunnerTunnelRegistry();
        foreach (var runner in db.Runners.AsNoTracking().ToList())
            tunnels.Add(runner.Id, new TunnelConnectionStream(new MemoryStream()), out _);
        return new RuntimeTunnelService(db, tunnels,
            Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" }),
            identity, NullLogger<RuntimeTunnelService>.Instance, new RuntimeTunnelTargetCache(TimeSpan.Zero, TimeProvider.System));
    }
}
