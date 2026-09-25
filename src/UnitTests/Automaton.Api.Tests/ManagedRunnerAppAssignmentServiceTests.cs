using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Automaton.Api.Tests.ManagedRunnerAppAssignmentTestAssertions;

namespace Automaton.Api.Tests;

public abstract class ManagedRunnerAppAssignmentTestBase
{
    protected ApplicationDbContext db = null!;
    protected ManagedRunnerAppAssignmentService service = null!;
    protected ResourceAuthorizationService authorization = null!;
    protected UserContextService userContext = null!;
    protected BackendRouteConfigService routes = null!;
    protected HostedRunner hosted = null!;
    protected Guid previousRunnerId;
    protected string routeRoot = null!;
    private protected ManagedRunnerAppAssignmentIdentitySaveFailureInterceptor saveFailure = null!;

    [SetUp]
    public void SetUp()
    {
        var persistence = ManagedRunnerAppAssignmentTestPersistence.Create();
        db = persistence.Db;
        routeRoot = persistence.RouteRoot;
        saveFailure = persistence.SaveFailure;

        var services = ManagedRunnerAppAssignmentTestServices.Create(db, routeRoot);
        service = services.Service;
        userContext = services.UserContext;
        authorization = services.Authorization;
        routes = services.Routes;

        var seed = ManagedRunnerAppAssignmentTestSeed.Create(db);
        hosted = seed.Hosted;
        previousRunnerId = seed.PreviousRunnerId;
    }

    [TearDown]
    public void TearDown()
    {
        db.Dispose();
        Directory.Delete(routeRoot, recursive: true);
    }
}

[TestFixture]
public sealed class ManagedRunnerAppAssignmentReconciliationTests : ManagedRunnerAppAssignmentTestBase
{
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task Reconcile_ChangesOnlyRunnerIdentity_OnAllTargets(bool generic, bool legacy)
    {
        var target = AddTarget(db, hosted, previousRunnerId);
        target.GenericRuntimeDeployed = generic;
        target.Status = DeploymentTargetStatus.Deploying;
        target.ActiveDeploymentOperationId = Guid.NewGuid();
        target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
        target.LastDeploymentAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        target.LastSuccessfulDeploymentAt = DateTimeOffset.UtcNow.AddHours(-1);
        target.CleanupPending = true;
        target.LastCleanupError = "previous cleanup diagnostic";
        target.LastCleanupAttemptAt = DateTimeOffset.UtcNow.AddDays(-1);
        var secondary = AddSecondaryTarget(db, previousRunnerId, target);
        await db.SaveChangesAsync();
        var before = CaptureTargetProperties(db, target);

        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId!.Value), Is.EqualTo(2));
        db.ChangeTracker.Clear();
        var persisted = await db.AppDeploymentTargets.SingleAsync(x => x.Id == target.Id);
        Assert.That(persisted.ExecutionRunnerId, Is.EqualTo(hosted.RunnerId));
        AssertTargetPropertiesPreserved(db, persisted, before);
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
            if (scenario == "different-hosted-runner")
                ManagedRunnerAppAssignmentTestSeed.AddEnrollment(db, Guid.NewGuid(), oldId);
        }
        if (scenario == "different-owner") ownerId = Guid.NewGuid();
        if (scenario == "deleted-hosted-runner") hosted.DeletedAt = DateTime.UtcNow;
        if (scenario == "wrong-connecting-user") connectingUser = Guid.NewGuid();
        if (scenario == "old-connecting-runner") connectingRunner = previousRunnerId;
        var target = AddTarget(db, hosted, oldId, ownerId);
        await db.SaveChangesAsync();

        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(connectingUser, connectingRunner), Is.Zero);
        Assert.That(target.ExecutionRunnerId, Is.EqualTo(oldId));
        Assert.That(target.GenericRuntimeDeployed, Is.True);
    }

}

[TestFixture]
public sealed class ManagedRunnerConnectionLifecycleTests : ManagedRunnerAppAssignmentTestBase
{
    [Test]
    public async Task UpdateConnection_RebindsAndRefreshesPublicRoutes_AndRepeatedReconnectIsHarmless()
    {
        var target = AddTarget(db, hosted, previousRunnerId);
        await db.SaveChangesAsync();
        await routes.RefreshAsync();
        var routeFile = Path.Combine(routeRoot, "managed-runtime-routes.yml");
        Assert.That(await File.ReadAllTextAsync(routeFile), Does.Not.Contain(hosted.RuntimeEndpoint));
        var runners = ManagedRunnerConnectionTestServiceFactory.Create(db, userContext, authorization, service);

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
    public async Task DisconnectOfSupersededConnection_CannotClobberCurrentConnection()
    {
        var runners = ManagedRunnerConnectionTestServiceFactory.Create(db, userContext, authorization, service);

        await runners.UpdateConnection(hosted.RunnerId!.Value, hosted.UserId, new RunnerDetails
        {
            ConnectionId = "connection-a", HostedRunnerId = hosted.Id
        }, CancellationToken.None);
        await runners.UpdateConnection(hosted.RunnerId.Value, hosted.UserId, new RunnerDetails
        {
            ConnectionId = "connection-b", HostedRunnerId = hosted.Id
        }, CancellationToken.None);

        await runners.MarkDisconnectedAsync(hosted.RunnerId.Value, "connection-a", CancellationToken.None);

        db.ChangeTracker.Clear();
        var persisted = await db.Runners.SingleAsync(x => x.Id == hosted.RunnerId.Value);
        Assert.Multiple(() =>
        {
            Assert.That(persisted.ConnectionId, Is.EqualTo("connection-b"));
            Assert.That(persisted.Status, Is.EqualTo(RunnerConnectivityStatuses.Online));
        });
    }

    [Test]
    public async Task UpdateConnection_CompletesDespiteRouteFailure_AndLaterReconciliationRepairsRoute()
    {
        var target = AddTarget(db, hosted, previousRunnerId);
        target.ActiveDeploymentOperationId = Guid.NewGuid();
        target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
        await db.SaveChangesAsync();
        var before = CaptureTargetProperties(db, target);
        var failingRoutes = new ManagedRunnerAppAssignmentFailingRouteService(db);
        var failingService = ManagedRunnerConnectionTestServiceFactory.CreateWithFailingRoutes(
            db, authorization, failingRoutes);
        var runners = ManagedRunnerConnectionTestServiceFactory.Create(db, userContext, authorization, failingService);

        await runners.UpdateConnection(hosted.RunnerId!.Value, hosted.UserId,
            CreateRunnerDetails("connected-despite-route-failure", hosted.Id), CancellationToken.None);

        AssertRouteFailureObserved(failingRoutes, hosted);
        db.ChangeTracker.Clear();
        var persisted = await db.AppDeploymentTargets.SingleAsync(x => x.Id == target.Id);
        Assert.That(persisted.ExecutionRunnerId, Is.EqualTo(hosted.RunnerId));
        AssertTargetPropertiesPreserved(db, persisted, before);
        await AssertRunnerLifecycleContinuedAsync(db, hosted, target);

        await AssertRouteRepairedAsync(service, hosted, routeRoot);
    }

    [Test]
    public async Task Reconcile_DoesNotSwallowIdentityPersistenceFailure_OrPublishRoutes()
    {
        var target = AddTarget(db, hosted, previousRunnerId);
        await db.SaveChangesAsync();
        var failingRoutes = new ManagedRunnerAppAssignmentFailingRouteService(db);
        var failingService = new ManagedRunnerAppAssignmentService(db, null!, authorization,
            failingRoutes, NullLogger<ManagedRunnerAppAssignmentService>.Instance);
        saveFailure.FailIdentitySave = true;

        var exception = Assert.ThrowsAsync<DbUpdateException>(() =>
            failingService.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId!.Value));

        Assert.That(exception, Is.SameAs(saveFailure.Error));
        Assert.That(failingRoutes.Calls, Is.Zero);
        db.ChangeTracker.Clear();
        Assert.That((await db.AppDeploymentTargets.FindAsync(target.Id))!.ExecutionRunnerId, Is.EqualTo(previousRunnerId));
    }
}

[TestFixture]
public sealed class ManagedRunnerAppEligibilityTests : ManagedRunnerAppAssignmentTestBase
{
    [Test]
    public async Task AssignEligibleApps_StillAssignsUnassignedGenericApp()
    {
        var target = AddTarget(db, hosted, null);
        target.GenericRuntimeDeployed = false;
        (await db.Runners.SingleAsync(runner => runner.Id == hosted.RunnerId)).ApiCompatibilityVersion =
            RunnerApiCompatibilityVersions.ContainerApplicationExecution;
        target.WebApp.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract
        {
            Runtime = new ApplicationRuntimeSpec { Image = "test/runtime:1", Command = new ApplicationCommandSpec { Executable = "server" } }
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
        var target = AddTarget(db, hosted, previousRunnerId, Guid.NewGuid());
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
}
