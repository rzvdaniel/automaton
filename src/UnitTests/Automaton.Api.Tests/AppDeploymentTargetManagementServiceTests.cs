using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppDeploymentTargetManagementServiceTests
{
    [Test]
    public async Task CreateAndList_NormalizesSlugAndReturnsRunnerState()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(online: true);
        await fixture.Db.SaveChangesAsync();

        var created = await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest
            {
                Name = " Server EU ",
                Slug = " Server-EU ",
                RunnerId = runner.Id
            },
            fixture.UserId);
        var targets = await fixture.Service.ListAsync(fixture.App.Id, fixture.UserId);

        Assert.Multiple(() =>
        {
            Assert.That(created.Name, Is.EqualTo("Server EU"));
            Assert.That(created.Slug, Is.EqualTo("server-eu"));
            Assert.That(created.Runner?.Id, Is.EqualTo(runner.Id));
            Assert.That(created.Runner?.IsOnline, Is.True);
            Assert.That(created.RoutePath, Is.EqualTo(
                $"/a/{fixture.App.Id:D}/t/{created.Id:D}/"));
            Assert.That(targets, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task MakeDefault_SwitchesDefaultAtomically()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest { Name = "EU", Slug = "eu" },
            fixture.UserId);

        await fixture.Service.MakeDefaultAsync(fixture.App.Id, created.Id, fixture.UserId);

        var targets = await fixture.Db.AppDeploymentTargets
            .Where(target => target.WebAppId == fixture.App.Id)
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(targets.Count(target => target.IsDefault), Is.EqualTo(1));
            Assert.That(targets.Single(target => target.Id == created.Id).IsDefault, Is.True);
        });
    }

    [Test]
    public async Task Delete_DefaultTargetIsRejected()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);

        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await fixture.Service.DeleteAsync(
                fixture.App.Id, target.Id, fixture.UserId));

        Assert.That(exception!.Message, Does.Contain("default"));
    }

    [Test]
    public async Task AssignRunner_WhileDeployingIsRejected()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(online: true);
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);
        target.Status = DeploymentTargetStatus.Deploying;
        await fixture.Db.SaveChangesAsync();

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await fixture.Service.AssignRunnerAsync(
                fixture.App.Id,
                target.Id,
                new AssignAppDeploymentTargetRunnerRequest { RunnerId = runner.Id },
                fixture.UserId));
    }

    [Test]
    public async Task AssignRunner_DeployedTargetRequiresExplicitCleanup()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var oldRunner = fixture.AddRunner(online: true);
        var newRunner = fixture.AddRunner(online: true);
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);
        target.ExecutionRunnerId = oldRunner.Id;
        target.FrontendDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        await fixture.Db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await fixture.Service.AssignRunnerAsync(
                fixture.App.Id,
                target.Id,
                new AssignAppDeploymentTargetRunnerRequest { RunnerId = newRunner.Id },
                fixture.UserId));

        Assert.That(exception!.Message, Does.Contain("Explicit runtime cleanup"));
    }

    [Test]
    public async Task CleanupFailure_IsRecordedAndCanBeRetried()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(online: false);
        await fixture.Db.SaveChangesAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest
            {
                Name = "Offline target",
                Slug = "offline-target",
                RunnerId = runner.Id
            },
            fixture.UserId);
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(candidate => candidate.Id == created.Id);
        target.FrontendDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        await fixture.Db.SaveChangesAsync();

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await fixture.Service.DeleteAsync(
                fixture.App.Id, target.Id, fixture.UserId));

        Assert.Multiple(() =>
        {
            Assert.That(target.CleanupPending, Is.True);
            Assert.That(target.LastCleanupAttemptAt, Is.Not.Null);
            Assert.That(target.LastCleanupError, Does.Contain("must be online"));
            Assert.That(fixture.Db.AppDeploymentTargets.Any(candidate => candidate.Id == target.Id), Is.True);
        });
    }

    [Test]
    public async Task OfflineRunner_IsRepresentedWithoutClearingDeploymentMetadata()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(online: false);
        await fixture.Db.SaveChangesAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest
            {
                Name = "Offline target",
                Slug = "offline-target",
                RunnerId = runner.Id
            },
            fixture.UserId);
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(candidate => candidate.Id == created.Id);
        target.FrontendDeployed = true;
        target.LastSuccessfulDeploymentAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        target.Status = DeploymentTargetStatus.Running;
        await fixture.Db.SaveChangesAsync();

        var summary = await fixture.Service.GetAsync(fixture.App.Id, target.Id, fixture.UserId);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Status, Is.EqualTo(DeploymentTargetStatus.RunnerOffline));
            Assert.That(summary.FrontendDeployed, Is.True);
            Assert.That(summary.LastSuccessfulDeploymentAt, Is.EqualTo(target.LastSuccessfulDeploymentAt));
        });
    }

    [Test]
    public async Task AppDeletionClaim_BlocksDeploymentUntilReleased()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);

        var claims = await fixture.Service.ClaimAndCleanupAppRuntimesAsync(fixture.App.Id);

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await new AppDeploymentTargetStateService(fixture.Db)
                .MarkStartedAsync(fixture.App.Id, target.Id));

        await fixture.Service.ReleaseAppDeletionClaimsAsync(fixture.App.Id, claims);
        Assert.That(
            (await fixture.Db.AppDeploymentTargets.SingleAsync(candidate => candidate.Id == target.Id)).Status,
            Is.EqualTo(DeploymentTargetStatus.Unassigned));
    }

    [Test]
    public async Task Create_InfrastructureRunnerForOrdinaryAppIsRejected()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(online: true, purpose: RunnerPurposes.Infrastructure);
        await fixture.Db.SaveChangesAsync();

        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await fixture.Service.CreateAsync(
                fixture.App.Id,
                new CreateAppDeploymentTargetRequest
                {
                    Name = "Infrastructure",
                    Slug = "infrastructure",
                    RunnerId = runner.Id
                },
                fixture.UserId));
    }

    [Test]
    public async Task TargetDeletion_PreservesExecutionHistoryByClearingTargetReference()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var foreignKey = fixture.Db.Model.FindEntityType(typeof(AppModuleExecution))!
            .GetForeignKeys()
            .Single(candidate => candidate.Properties.Any(
                property => property.Name == nameof(AppModuleExecution.DeploymentTargetId)));

        Assert.That(foreignKey.DeleteBehavior, Is.EqualTo(DeleteBehavior.SetNull));
    }

    private sealed class TestFixtureContext : IAsyncDisposable
    {
        private readonly string routeRoot;

        public Guid UserId { get; }
        public ApplicationDbContext Db { get; }
        public WebApp App { get; }
        public AppDeploymentTargetManagementService Service { get; }

        private TestFixtureContext(
            ApplicationDbContext db,
            WebApp app,
            AppDeploymentTargetManagementService service,
            string routeRoot,
            Guid userId)
        {
            Db = db;
            App = app;
            Service = service;
            this.routeRoot = routeRoot;
            UserId = userId;
        }

        public static async Task<TestFixtureContext> CreateAsync()
        {
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase($"target-management-{Guid.NewGuid():N}")
                    .Options);

            var userId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var app = new WebApp
            {
                Id = appId,
                Name = "Target API App",
                Summary = string.Empty,
                Description = string.Empty,
                Definition = "{}",
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow,
                DeploymentTargets = { AppDeploymentTarget.CreateDefault(appId) }
            };
            db.WebApps.Add(app);
            db.WebAppUsers.Add(new WebAppUser { WebAppId = appId, UserId = userId });
            await db.SaveChangesAsync();

            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                        "Test"))
                }
            };
            var authorization = new ResourceAuthorizationService(
                db, new UserContextService(accessor), accessor);
            var routeRoot = Path.Combine(
                Path.GetTempPath(), "automaton-tests", Guid.NewGuid().ToString("N"));
            var routeService = new BackendRouteConfigService(
                db,
                Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = routeRoot }),
                new StubWebHostEnvironment(),
                NullLogger<BackendRouteConfigService>.Instance);
            var service = new AppDeploymentTargetManagementService(
                db,
                authorization,
                new AppRunnerAuthorizationPolicy(db, authorization),
                new RejectingHubContext(),
                routeService,
                new AppDeploymentTargetStateService(db));
            return new TestFixtureContext(db, app, service, routeRoot, userId);
        }

        public Runner AddRunner(bool online, string purpose = RunnerPurposes.Managed)
        {
            var runner = new Runner
            {
                Id = Guid.NewGuid(),
                Name = $"Runner {Guid.NewGuid():N}",
                Purpose = purpose,
                CapabilitiesJson = RunnerCapabilities.Serialize(
                    RunnerCapabilities.DefaultsForPurpose(purpose), purpose),
                ConnectionId = online ? $"connection-{Guid.NewGuid():N}" : null,
                Status = online ? RunnerStatus.Online.ToString() : RunnerStatus.Offline.ToString()
            };
            Db.Runners.Add(runner);
            Db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = UserId });
            return runner;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(routeRoot))
            {
                Directory.Delete(routeRoot, recursive: true);
            }
        }
    }

    private sealed class RejectingHubContext : IHubContext<AutomatonHub>
    {
        public IHubClients Clients { get; } = new RejectingHubClients();
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class RejectingHubClients : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public ISingleClientProxy Client(string connectionId) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
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
