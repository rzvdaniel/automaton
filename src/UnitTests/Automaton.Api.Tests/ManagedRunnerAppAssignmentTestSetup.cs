using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class ManagedRunnerAppAssignmentTestPersistence
{
    public static ManagedRunnerAppAssignmentPersistence Create()
    {
        var saveFailure = new ManagedRunnerAppAssignmentIdentitySaveFailureInterceptor();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(saveFailure).Options);
        var routeRoot = Path.Combine(Path.GetTempPath(), "automaton-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(routeRoot);
        return new(db, routeRoot, saveFailure);
    }
}

internal static class ManagedRunnerAppAssignmentTestServices
{
    public static ManagedRunnerAppAssignmentServices Create(ApplicationDbContext db, string routeRoot)
    {
        var userContext = new UserContextService(new HttpContextAccessor());
        var authorization = new ResourceAuthorizationService(db, userContext);
        var runnerAuthorization = new AppRunnerAuthorizationPolicy(db, authorization);
        var assignment = new AppRunnerAssignmentPolicy(db, new HostedRunnerAppCapacityService(db),
            runnerAuthorization, new AppDeploymentTargetResolver(db, authorization, runnerAuthorization));
        var routes = new BackendRouteConfigService(db,
            Options.Create(new PublishedAppsConfig { TraefikDynamicConfigRoot = routeRoot }),
            new ManagedRunnerAppAssignmentTestEnvironment(), NullLogger<BackendRouteConfigService>.Instance);
        var service = new ManagedRunnerAppAssignmentService(db, assignment, authorization, routes,
            NullLogger<ManagedRunnerAppAssignmentService>.Instance);
        return new(userContext, authorization, routes, service);
    }
}

internal static class ManagedRunnerAppAssignmentTestSeed
{
    public static ManagedRunnerAppAssignmentSeed Create(ApplicationDbContext db)
    {
        var hosted = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerId = Guid.NewGuid(),
            Name = "Stable hosted resource", ContainerName = "hosted-container",
            RuntimeEndpoint = "http://hosted-container:8080", Status = HostedRunnerStatuses.Running
        };
        hosted.SpecificationSnapshot.Resources.MaximumApps = 10;
        var previousRunnerId = Guid.NewGuid();
        db.HostedRunners.Add(hosted);
        db.Runners.AddRange(
            new Runner { Id = previousRunnerId, Name = "old-name", Purpose = RunnerPurposes.Managed },
            new Runner { Id = hosted.RunnerId.Value, Name = "new-name", Purpose = RunnerPurposes.Managed });
        db.RunnerUsers.Add(new RunnerUser { RunnerId = hosted.RunnerId.Value, UserId = hosted.UserId });
        AddEnrollment(db, hosted.Id, previousRunnerId);
        AddEnrollment(db, hosted.Id, hosted.RunnerId.Value);
        db.SaveChanges();
        return new(hosted, previousRunnerId);
    }

    public static void AddEnrollment(ApplicationDbContext db, Guid hostedId, Guid runnerId) =>
        db.RunnerEnrollments.Add(new RunnerEnrollment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedId, RunnerId = runnerId,
            RunnerHostId = Guid.NewGuid(), Status = RunnerEnrollmentStatuses.Completed,
            UsedAtUtc = DateTime.UtcNow, InstallationId = Guid.NewGuid().ToString("N")
        });
}

internal sealed record ManagedRunnerAppAssignmentPersistence(
    ApplicationDbContext Db,
    string RouteRoot,
    ManagedRunnerAppAssignmentIdentitySaveFailureInterceptor SaveFailure);

internal sealed record ManagedRunnerAppAssignmentServices(
    UserContextService UserContext,
    ResourceAuthorizationService Authorization,
    BackendRouteConfigService Routes,
    ManagedRunnerAppAssignmentService Service);

internal sealed record ManagedRunnerAppAssignmentSeed(HostedRunner Hosted, Guid PreviousRunnerId);

internal sealed class ManagedRunnerAppAssignmentIdentitySaveFailureInterceptor : SaveChangesInterceptor
{
    public bool FailIdentitySave { get; set; }
    public DbUpdateException Error { get; } = new("Identity persistence failed");

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailIdentitySave && eventData.Context!.ChangeTracker.Entries<AppDeploymentTarget>()
            .Any(entry => entry.Property(target => target.ExecutionRunnerId).IsModified))
            throw Error;
        return ValueTask.FromResult(result);
    }
}

internal sealed class ManagedRunnerAppAssignmentTestEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Automaton.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = "Production";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

internal sealed class ManagedRunnerAppAssignmentFailingRouteService(ApplicationDbContext db)
    : BackendRouteConfigService(db, Options.Create(new PublishedAppsConfig()), new ManagedRunnerAppAssignmentTestEnvironment(),
        NullLogger<BackendRouteConfigService>.Instance)
{
    private readonly ApplicationDbContext database = db;
    public int Calls { get; private set; }
    public Guid? PersistedRunnerId { get; private set; }

    public override Task RefreshAsync(CancellationToken cancellationToken = default,
        Guid? pendingGenericRuntimeTargetId = null, bool? pendingGenericRuntimeDeployed = null)
    {
        Calls++;
        PersistedRunnerId = database.AppDeploymentTargets.AsNoTracking().Single().ExecutionRunnerId;
        throw new IOException("Route write failed");
    }
}

internal static class ManagedRunnerAppAssignmentTestAssertions
{
    public static RunnerDetails CreateRunnerDetails(string connectionId, Guid hostedRunnerId) => new()
    {
        ConnectionId = connectionId,
        HostedRunnerId = hostedRunnerId,
        Version = "test-version",
        MachineName = "test-machine"
    };

    public static AppDeploymentTarget AddTarget(
        ApplicationDbContext db,
        HostedRunner hosted,
        Guid? runnerId,
        Guid? ownerId = null)
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

    public static AppDeploymentTarget AddSecondaryTarget(
        ApplicationDbContext db,
        Guid previousRunnerId,
        AppDeploymentTarget target)
    {
        var secondary = AppDeploymentTarget.CreateLive(target.WebAppId, previousRunnerId);
        secondary.IsDefault = false;
        secondary.Slug = "staging";
        secondary.SynchronizeDefaultConstraint();
        db.AppDeploymentTargets.Add(secondary);
        return secondary;
    }

    public static Dictionary<string, object?> CaptureTargetProperties(
        ApplicationDbContext db,
        AppDeploymentTarget target) => db.Entry(target).CurrentValues.Properties
            .Where(property => property.Name != nameof(AppDeploymentTarget.ExecutionRunnerId))
            .ToDictionary(property => property.Name, property => db.Entry(target).Property(property.Name).CurrentValue);

    public static void AssertTargetPropertiesPreserved(
        ApplicationDbContext db,
        AppDeploymentTarget target,
        Dictionary<string, object?> before)
    {
        foreach (var (name, value) in before)
            Assert.That(db.Entry(target).Property(name).CurrentValue, Is.EqualTo(value), name);
    }

    public static void AssertRouteFailureObserved(
        ManagedRunnerAppAssignmentFailingRouteService failingRoutes,
        HostedRunner runner)
    {
        Assert.That(failingRoutes.Calls, Is.EqualTo(1));
        Assert.That(failingRoutes.PersistedRunnerId, Is.EqualTo(runner.RunnerId),
            "Identity must be saved before route publication");
    }

    public static async Task AssertRunnerLifecycleContinuedAsync(
        ApplicationDbContext db,
        HostedRunner hosted,
        AppDeploymentTarget target)
    {
        var runner = await db.Runners.SingleAsync(x => x.Id == hosted.RunnerId);
        Assert.Multiple(() =>
        {
            Assert.That(runner.ConnectionId, Is.EqualTo("connected-despite-route-failure"));
            Assert.That(runner.Status, Is.EqualTo(RunnerConnectivityStatuses.Online));
            Assert.That(runner.LastConnectedAtUtc, Is.Not.Null);
            Assert.That(runner.LastHeartbeatAtUtc, Is.Not.Null);
            Assert.That(runner.Version, Is.EqualTo("test-version"));
            Assert.That(runner.MachineName, Is.EqualTo("test-machine"));
        });
        Assert.That(await db.ApplicationEvents.AnyAsync(x => x.AppId == target.WebAppId), Is.True,
            "Connection lifecycle continues after reconciliation");
    }

    public static async Task AssertRouteRepairedAsync(
        ManagedRunnerAppAssignmentService service,
        HostedRunner hosted,
        string routeRoot)
    {
        Assert.That(await service.ReconcileHostedRunnerIdentityAsync(hosted.UserId, hosted.RunnerId!.Value), Is.Zero);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(routeRoot, "managed-runtime-routes.yml")),
            Does.Contain(hosted.RuntimeEndpoint));
    }
}
