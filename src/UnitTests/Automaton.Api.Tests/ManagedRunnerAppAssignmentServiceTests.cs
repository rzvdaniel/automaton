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
public class ManagedRunnerAppAssignmentServiceTests
{
    private ApplicationDbContext db = null!;
    private ManagedRunnerAppAssignmentService service = null!;
    private ResourceAuthorizationService authorization = null!;
    private UserContextService userContext = null!;
    private BackendRouteConfigService routes = null!;
    private HostedRunner hosted = null!;
    private Guid previousRunnerId;
    private string routeRoot = null!;

    [SetUp]
    public void SetUp()
    {
        db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        routeRoot = Path.Combine(Path.GetTempPath(), "automaton-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(routeRoot);
        userContext = new UserContextService(new HttpContextAccessor());
        authorization = new ResourceAuthorizationService(db, userContext);
        var runnerAuthorization = new AppRunnerAuthorizationPolicy(db, authorization);
        var assignment = new AppRunnerAssignmentPolicy(db, new HostedRunnerAppCapacityService(db),
            runnerAuthorization, new AppDeploymentTargetResolver(db, authorization, runnerAuthorization));
        routes = new BackendRouteConfigService(db,
            Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = routeRoot }),
            new TestEnvironment(), NullLogger<BackendRouteConfigService>.Instance);
        service = new ManagedRunnerAppAssignmentService(db, assignment, authorization, routes,
            NullLogger<ManagedRunnerAppAssignmentService>.Instance);
        hosted = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerId = Guid.NewGuid(),
            Name = "Stable hosted resource", ContainerName = "hosted-container",
            RuntimeEndpoint = "http://hosted-container:8080", Status = HostedRunnerStatuses.Running
        };
        hosted.SpecificationSnapshot.Resources.MaximumApps = 10;
        previousRunnerId = Guid.NewGuid();
        db.HostedRunners.Add(hosted);
        db.Runners.AddRange(
            new Runner { Id = previousRunnerId, Name = "old-name", Purpose = RunnerPurposes.Managed },
            new Runner { Id = hosted.RunnerId.Value, Name = "new-name", Purpose = RunnerPurposes.Managed });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = hosted.RunnerId.Value, UserId = hosted.UserId });
        AddEnrollment(hosted.Id, previousRunnerId);
        AddEnrollment(hosted.Id, hosted.RunnerId.Value);
        db.SaveChanges();
    }

    [TearDown]
    public void TearDown()
    {
        db.Dispose();
        Directory.Delete(routeRoot, recursive: true);
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Reconcile_ChangesOnlyRunnerIdentity_OnAllTargets(bool generic, bool legacy)
    {
        var target = AddTarget(previousRunnerId);
        target.GenericRuntimeDeployed = generic;
        target.LegacyControllerRuntimeDeployed = legacy;
        target.Status = DeploymentTargetStatus.Deploying;
        target.ActiveDeploymentOperationId = Guid.NewGuid();
        target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
        target.LastDeploymentAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        target.LastSuccessfulDeploymentAt = DateTimeOffset.UtcNow.AddHours(-1);
        target.CleanupPending = true;
        target.LastCleanupError = "previous cleanup diagnostic";
        target.LastCleanupAttemptAt = DateTimeOffset.UtcNow.AddDays(-1);
        var secondary = AppDeploymentTarget.CreateLive(target.WebAppId, previousRunnerId);
        secondary.IsDefault = false;
        secondary.Slug = "staging";
        secondary.SynchronizeDefaultConstraint();
        db.AppDeploymentTargets.Add(secondary);
        await db.SaveChangesAsync();
        var before = db.Entry(target).CurrentValues.Properties
            .Where(x => x.Name != nameof(AppDeploymentTarget.ExecutionRunnerId))
            .ToDictionary(x => x.Name, x => db.Entry(target).Property(x.Name).CurrentValue);

        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId!.Value), Is.EqualTo(2));
        db.ChangeTracker.Clear();
        var persisted = await db.AppDeploymentTargets.SingleAsync(x => x.Id == target.Id);
        Assert.That(persisted.ExecutionRunnerId, Is.EqualTo(hosted.RunnerId));
        foreach (var (name, value) in before)
            Assert.That(db.Entry(persisted).Property(name).CurrentValue, Is.EqualTo(value), name);
        Assert.That((await db.AppDeploymentTargets.FindAsync(secondary.Id))!.ExecutionRunnerId, Is.EqualTo(hosted.RunnerId));
        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId.Value), Is.Zero);
    }

    [TestCase("self-hosted")]
    [TestCase("different-hosted-runner")]
    [TestCase("different-owner")]
    [TestCase("deleted-hosted-runner")]
    [TestCase("wrong-connecting-user")]
    [TestCase("old-connecting-runner")]
    public async Task Reconcile_DoesNotRebindUnrelatedAssignments(string scenario)
    {
        var oldId = previousRunnerId;
        var ownerId = hosted.UserId;
        var connectingUser = hosted.UserId;
        var connectingRunner = hosted.RunnerId!.Value;
        if (scenario is "self-hosted" or "different-hosted-runner")
        {
            oldId = Guid.NewGuid();
            db.Runners.Add(new Runner { Id = oldId, Name = hosted.Name, Purpose = RunnerPurposes.Managed });
            if (scenario == "different-hosted-runner") AddEnrollment(Guid.NewGuid(), oldId);
        }
        if (scenario == "different-owner") ownerId = Guid.NewGuid();
        if (scenario == "deleted-hosted-runner") hosted.DeletedAt = DateTime.UtcNow;
        if (scenario == "wrong-connecting-user") connectingUser = Guid.NewGuid();
        if (scenario == "old-connecting-runner") connectingRunner = previousRunnerId;
        var target = AddTarget(oldId, ownerId);
        await db.SaveChangesAsync();

        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(connectingUser, connectingRunner), Is.Zero);
        Assert.That(target.ExecutionRunnerId, Is.EqualTo(oldId));
        Assert.That(target.GenericRuntimeDeployed, Is.True);
    }

    [Test]
    public async Task UpdateConnection_RebindsAndRefreshesPublicRoutes_AndRepeatedReconnectIsHarmless()
    {
        var target = AddTarget(previousRunnerId);
        await db.SaveChangesAsync();
        await routes.RefreshAsync();
        var routeFile = Path.Combine(routeRoot, "managed-runtime-routes.yml");
        Assert.That(await File.ReadAllTextAsync(routeFile), Does.Not.Contain(hosted.RuntimeEndpoint));
        var runners = new RunnerService(db, userContext, null!, authorization,
            new ApiInstanceIdentityService(), new AuditEventService(db, userContext, new HttpContextAccessor()),
            new ApplicationEventService(db), service);

        for (var connection = 0; connection < 2; connection++)
        {
            await runners.UpdateConnection(hosted.RunnerId!.Value, hosted.UserId, new RunnerDetails
            {
                ConnectionId = $"connection-{connection}", HostedRunnerId = hosted.Id
            }, CancellationToken.None);

            Assert.That(target.ExecutionRunnerId, Is.EqualTo(hosted.RunnerId));
            Assert.That(target.GenericRuntimeDeployed, Is.True);
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Running));
            var content = await File.ReadAllTextAsync(routeFile);
            Assert.That(content, Does.Contain(hosted.RuntimeEndpoint));
            Assert.That(content, Does.Contain($"/apps/{target.WebAppId}/targets/{target.Id}/$1"));
        }
    }

    [Test]
    public async Task Reconcile_RefreshesAlreadyReboundTargets_AfterRouteWriteFailure()
    {
        var target = AddTarget(previousRunnerId);
        await db.SaveChangesAsync();
        var failingService = new ManagedRunnerAppAssignmentService(db, null!, authorization,
            new FailingRouteService(db), NullLogger<ManagedRunnerAppAssignmentService>.Instance);
        Assert.ThrowsAsync<IOException>(() => failingService.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId!.Value));
        Assert.That(target.ExecutionRunnerId, Is.EqualTo(hosted.RunnerId));

        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId!.Value), Is.Zero);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(routeRoot, "managed-runtime-routes.yml")),
            Does.Contain(hosted.RuntimeEndpoint));
    }

    [Test]
    public async Task AssignEligibleApps_StillAssignsUnassignedGenericApp()
    {
        var target = AddTarget(null);
        target.GenericRuntimeDeployed = false;
        target.WebApp.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec { Command = new ApplicationCommandSpec { Executable = "server" } }
        });
        await db.SaveChangesAsync();

        Assert.That(await service.AssignEligibleAppsAsync(hosted.UserId, hosted.RunnerId!.Value), Is.EqualTo(1));
        Assert.That(target.ExecutionRunnerId, Is.EqualTo(hosted.RunnerId));
        Assert.That(target.GenericRuntimeDeployed, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Reconcile_UsesExistingProjectAccessRules(bool activeMember)
    {
        var target = AddTarget(previousRunnerId, Guid.NewGuid());
        var project = new Project { Id = Guid.NewGuid(), Name = "shared-project" };
        project.Members.Add(new ProjectMember
        {
            ProjectId = project.Id, UserId = hosted.UserId, Role = ProjectRole.Developer, IsActive = activeMember
        });
        project.ProjectApps.Add(new ProjectApp { ProjectId = project.Id, WebAppId = target.WebAppId });
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId!.Value),
            Is.EqualTo(activeMember ? 1 : 0));
        Assert.That(target.ExecutionRunnerId, Is.EqualTo(activeMember ? hosted.RunnerId : previousRunnerId));
    }

    private AppDeploymentTarget AddTarget(Guid? runnerId, Guid? ownerId = null)
    {
        var app = new WebApp
        {
            Id = Guid.NewGuid(), Name = "Existing app", Summary = "", Description = "", Definition = "{}",
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
            WebAppUsers = new List<WebAppUser>()
        };
        app.WebAppUsers.Add(new WebAppUser { WebAppId = app.Id, UserId = ownerId ?? hosted.UserId });
        var target = AppDeploymentTarget.CreateLive(app.Id, runnerId);
        target.GenericRuntimeDeployed = true;
        target.Status = DeploymentTargetStatus.Running;
        app.DeploymentTargets.Add(target);
        db.WebApps.Add(app);
        return target;
    }

    private void AddEnrollment(Guid hostedId, Guid runnerId) => db.RunnerEnrollments.Add(new RunnerEnrollment
    {
        Id = Guid.NewGuid(), HostedRunnerId = hostedId, RunnerId = runnerId,
        RunnerHostId = Guid.NewGuid(), Status = RunnerEnrollmentStatuses.Completed,
        UsedAtUtc = DateTime.UtcNow, InstallationId = Guid.NewGuid().ToString("N")
    });

    private sealed class FailingRouteService(ApplicationDbContext db)
        : BackendRouteConfigService(db, Options.Create(new PublishedAppsConfig()), new TestEnvironment(),
            NullLogger<BackendRouteConfigService>.Instance)
    {
        public override Task RefreshAsync(CancellationToken cancellationToken = default,
            Guid? pendingGenericRuntimeTargetId = null, bool? pendingGenericRuntimeDeployed = null) =>
            throw new IOException("Route write failed");
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
