using System.Security.Claims;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
            Assert.That(targets.Single(x => x.IsDefault).RoutePath, Is.EqualTo("/"));
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
    public async Task Delete_LiveTargetIsRejected()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);

        var exception = Assert.ThrowsAsync<AppDeploymentTargetConflictException>(
            async () => await fixture.Service.DeleteAsync(
                fixture.App.Id, target.Id, fixture.UserId));

        Assert.That(exception!.Message, Does.Contain("Live"));
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
        target.GenericRuntimeDeployed = true;
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
    public async Task AssignRunner_AfterCleanupClearsGenericRuntimePromotion()
    {
        await using var fixture = await TestFixtureContext.CreateAsync(successfulCleanup: true);
        var oldRunner = fixture.AddRunner(online: true);
        var newRunner = fixture.AddRunner(online: true);
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);
        target.ExecutionRunnerId = oldRunner.Id;
        target.GenericRuntimeDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.AssignRunnerAsync(
            fixture.App.Id,
            target.Id,
            new AssignAppDeploymentTargetRunnerRequest
            {
                RunnerId = newRunner.Id,
                CleanupDeployedRuntime = true
            },
            fixture.UserId);

        Assert.Multiple(() =>
        {
            Assert.That(target.ExecutionRunnerId, Is.EqualTo(newRunner.Id));
            Assert.That(target.GenericRuntimeDeployed, Is.False);
        });
    }

    [Test]
    public async Task CleanupFailure_IsRecordedAndCanBeRetried()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var target = await AppDeploymentTargetTestScenarios.CreateRunningTargetAsync(
            fixture,
            "Offline target",
            "offline-target");

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
        target.LastSuccessfulDeploymentAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        target.GenericRuntimeDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        await fixture.Db.SaveChangesAsync();

        var summary = await fixture.Service.GetAsync(fixture.App.Id, target.Id, fixture.UserId);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Status, Is.EqualTo(DeploymentTargetStatus.RunnerOffline));
            Assert.That(summary.GenericRuntimeDeployed, Is.True);
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
    public async Task Create_PrivilegedHostRequiresPersistedApproval()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(
            online: true,
            applicationProfile: RunnerApplicationProfiles.PrivilegedHost);
        await fixture.Db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.Service.CreateAsync(
                fixture.App.Id,
                new CreateAppDeploymentTargetRequest
                {
                    Name = "Privileged",
                    Slug = "privileged",
                    RunnerId = runner.Id
                },
                fixture.UserId));
        var created = await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest
            {
                Name = "Confirmed privileged",
                Slug = "confirmed-privileged"
            },
            fixture.UserId);
        await fixture.Service.SetPrivilegedHostApprovalAsync(fixture.App.Id, created.Id, fixture.UserId, true);
        created = await fixture.Service.AssignRunnerAsync(
            fixture.App.Id,
            created.Id,
            new AssignAppDeploymentTargetRunnerRequest { RunnerId = runner.Id },
            fixture.UserId);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("persisted Privileged Host access approval"));
            Assert.That(created.Runner?.Id, Is.EqualTo(runner.Id));
        });
    }

    [Test]
    public async Task PrivilegedHostApproval_IsIdempotentAndCanBeRevoked()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);

        var approved = await fixture.Service.SetPrivilegedHostApprovalAsync(
            fixture.App.Id, target.Id, fixture.UserId, true);
        var approvedAgain = await fixture.Service.SetPrivilegedHostApprovalAsync(
            fixture.App.Id, target.Id, fixture.UserId, true);
        var revoked = await fixture.Service.SetPrivilegedHostApprovalAsync(
            fixture.App.Id, target.Id, fixture.UserId, false);

        Assert.Multiple(() =>
        {
            Assert.That(approved.PrivilegedHostAccessApproved, Is.True);
            Assert.That(approved.PrivilegedHostAccessApprovedAtUtc, Is.Not.Null);
            Assert.That(approved.PrivilegedHostAccessApprovedByUserId, Is.EqualTo(fixture.UserId));
            Assert.That(approvedAgain.PrivilegedHostAccessApprovedAtUtc, Is.EqualTo(approved.PrivilegedHostAccessApprovedAtUtc));
            Assert.That(revoked.PrivilegedHostAccessApproved, Is.False);
            Assert.That(revoked.PrivilegedHostAccessApprovedAtUtc, Is.Null);
            Assert.That(revoked.PrivilegedHostAccessApprovedByUserId, Is.Null);
        });
    }

    [Test]
    public async Task AssignRunner_PrivilegedHostRequiresPersistedApprovalOnlyWhenRunnerChanges()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var privilegedRunner = fixture.AddRunner(
            online: true,
            applicationProfile: RunnerApplicationProfiles.PrivilegedHost);
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);
        await fixture.Db.SaveChangesAsync();

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.Service.AssignRunnerAsync(
                fixture.App.Id,
                target.Id,
                new AssignAppDeploymentTargetRunnerRequest { RunnerId = privilegedRunner.Id },
                fixture.UserId));

        await fixture.Service.SetPrivilegedHostApprovalAsync(fixture.App.Id, target.Id, fixture.UserId, true);
        var assigned = await fixture.Service.AssignRunnerAsync(
            fixture.App.Id,
            target.Id,
            new AssignAppDeploymentTargetRunnerRequest
            {
                RunnerId = privilegedRunner.Id
            },
            fixture.UserId);
        var unchanged = await fixture.Service.AssignRunnerAsync(
            fixture.App.Id,
            target.Id,
            new AssignAppDeploymentTargetRunnerRequest { RunnerId = privilegedRunner.Id },
            fixture.UserId);

        Assert.Multiple(() =>
        {
            Assert.That(assigned.Runner?.Id, Is.EqualTo(privilegedRunner.Id));
            Assert.That(unchanged.Runner?.Id, Is.EqualTo(privilegedRunner.Id));
        });
    }

    [Test]
    public async Task List_IdentifiesOnlyActiveHostedRunnerRelationshipsAsAutomatonHosted()
    {
        await using var fixture = await TestFixtureContext.CreateAsync();
        var activeRunner = fixture.AddRunner(online: true);
        var deletedRunner = fixture.AddRunner(online: true);
        fixture.Db.HostedRunners.AddRange(
            fixture.AddHostedRunner(activeRunner.Id),
            fixture.AddHostedRunner(deletedRunner.Id, DateTime.UtcNow));
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest
            {
                Name = "Active hosted",
                Slug = "active-hosted",
                RunnerId = activeRunner.Id
            },
            fixture.UserId);
        await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest
            {
                Name = "Deleted hosted",
                Slug = "deleted-hosted",
                RunnerId = deletedRunner.Id
            },
            fixture.UserId);

        var targets = await fixture.Service.ListAsync(fixture.App.Id, fixture.UserId);

        Assert.Multiple(() =>
        {
            Assert.That(targets.Single(target => target.Runner?.Id == activeRunner.Id).Runner!.IsHostedRunner, Is.True);
            Assert.That(targets.Single(target => target.Runner?.Id == deletedRunner.Id).Runner!.IsHostedRunner, Is.False);
        });
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

    internal sealed class TestFixtureContext : IAsyncDisposable
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

        public static async Task<TestFixtureContext> CreateAsync(bool successfulCleanup = false)
        {
            var state = await CreatePersistentStateAsync();
            var service = CreateService(state.Db, state.UserId, successfulCleanup, out var routeRoot);
            return new TestFixtureContext(state.Db, state.App, service, routeRoot, state.UserId);
        }

        private static async Task<PersistentState> CreatePersistentStateAsync()
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
            return new PersistentState(db, app, userId);
        }

        private static AppDeploymentTargetManagementService CreateService(
            ApplicationDbContext db,
            Guid userId,
            bool successfulCleanup,
            out string routeRoot)
        {
            var authorization = CreateAuthorizationService(db, userId);
            var routeService = CreateRouteService(db, out routeRoot);
            var cleanupCommands = new CleanupCommands(successfulCleanup);
            return new AppDeploymentTargetManagementService(
                db,
                authorization,
                new AppRunnerAuthorizationPolicy(db, authorization),
                routeService,
                new AppDeploymentTargetStateService(db),
                new AppDeploymentTargetRuntimeCleanupService(db, cleanupCommands));
        }

        private static ResourceAuthorizationService CreateAuthorizationService(
            ApplicationDbContext db,
            Guid userId)
        {
            var accessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                        "Test"))
                }
            };
            return new ResourceAuthorizationService(db, new UserContextService(accessor), accessor);
        }

        private static BackendRouteConfigService CreateRouteService(
            ApplicationDbContext db,
            out string routeRoot)
        {
            routeRoot = Path.Combine(Path.GetTempPath(), "automaton-tests", Guid.NewGuid().ToString("N"));
            return new BackendRouteConfigService(
                db,
                Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = routeRoot }),
                new StubWebHostEnvironment(),
                NullLogger<BackendRouteConfigService>.Instance);
        }

        private sealed record PersistentState(ApplicationDbContext Db, WebApp App, Guid UserId);

        public Runner AddRunner(
            bool online,
            string purpose = RunnerPurposes.Managed,
            string? applicationProfile = null)
        {
            var runner = new Runner
            {
                Id = Guid.NewGuid(),
                Name = $"Runner {Guid.NewGuid():N}",
                Purpose = purpose,
                ApplicationProfile = applicationProfile,
                CapabilitiesJson = RunnerCapabilities.Serialize(
                    RunnerCapabilities.DefaultsForPurpose(purpose), purpose),
                ConnectionId = online ? $"connection-{Guid.NewGuid():N}" : null,
                LastHeartbeatAtUtc = online ? DateTime.UtcNow : null,
                Status = online ? RunnerStatus.Online.ToString() : RunnerStatus.Offline.ToString()
            };
            Db.Runners.Add(runner);
            Db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = UserId });
            return runner;
        }


        public HostedRunner AddHostedRunner(Guid runnerId, DateTime? deletedAt = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            RunnerId = runnerId,
            Name = $"Hosted {Guid.NewGuid():N}",
            ContainerName = $"hosted-{Guid.NewGuid():N}",
            Status = HostedRunnerStatuses.Running,
            DeletedAt = deletedAt
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(routeRoot))
            {
                Directory.Delete(routeRoot, recursive: true);
            }
        }
    }

    private sealed class CleanupCommands(bool successful) : IRunnerActionCommands
    {
        public Task<T> SendAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token)
        {
            if (!successful) throw new InvalidOperationException("Runner cleanup failed.");
            var request = (CleanupAppDeploymentTargetRequest)payload;
            object result = new CleanupAppDeploymentTargetResult(request.AppId, request.DeploymentTargetId, true);
            return Task.FromResult((T)result);
        }
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

internal static class AppDeploymentTargetTestScenarios
{
    public static async Task<AppDeploymentTarget> CreateRunningTargetAsync(
        AppDeploymentTargetManagementServiceTests.TestFixtureContext fixture,
        string name,
        string slug)
    {
        var runner = fixture.AddRunner(online: false);
        await fixture.Db.SaveChangesAsync();
        var created = await fixture.Service.CreateAsync(
            fixture.App.Id,
            new CreateAppDeploymentTargetRequest
            {
                Name = name,
                Slug = slug,
                RunnerId = runner.Id
            },
            fixture.UserId);
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(candidate => candidate.Id == created.Id);
        target.Status = DeploymentTargetStatus.Running;
        target.GenericRuntimeDeployed = true;
        await fixture.Db.SaveChangesAsync();
        return target;
    }
}
