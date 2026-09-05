using System.Security.Claims;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppDeploymentTargetResolverTests
{
    [Test]
    public async Task ResolveAsync_WithoutSlug_ReturnsDefaultTarget()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(db, userId, online: true);
        var app = AddApp(db, userId, runner.Id);
        await db.SaveChangesAsync();

        var target = await CreateResolver(db, userId).ResolveAsync(app.Id, null, userId);

        Assert.Multiple(() =>
        {
            Assert.That(target.IsDefault, Is.True);
            Assert.That(target.Slug, Is.EqualTo("live"));
            Assert.That(target.ExecutionRunner?.Id, Is.EqualTo(runner.Id));
        });
    }

    [Test]
    public async Task ResolveAsync_WithSlug_ReturnsNamedTarget()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var defaultRunner = AddRunner(db, userId, online: true);
        var namedRunner = AddRunner(db, userId, online: true);
        var app = AddApp(db, userId, defaultRunner.Id);
        var namedTarget = CreateTarget(app.Id, "Server EU", "server-eu", namedRunner.Id);
        app.DeploymentTargets.Add(namedTarget);
        await db.SaveChangesAsync();

        var target = await CreateResolver(db, userId)
            .ResolveAsync(app.Id, " SERVER-EU ", userId);

        Assert.Multiple(() =>
        {
            Assert.That(target.Id, Is.EqualTo(namedTarget.Id));
            Assert.That(target.ExecutionRunner?.Id, Is.EqualTo(namedRunner.Id));
        });
    }

    [Test]
    public async Task ResolveAsync_WithUnknownSlug_Throws()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(db, userId, online: true);
        var app = AddApp(db, userId, runner.Id);
        await db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await CreateResolver(db, userId)
                .ResolveAsync(app.Id, "unknown", userId));

        Assert.That(exception!.Message, Does.Contain("unknown"));
    }

    [Test]
    public async Task ResolveAsync_DoesNotResolveTargetBelongingToAnotherApp()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(db, userId, online: true);
        var firstApp = AddApp(db, userId, runner.Id);
        var secondApp = AddApp(db, userId, runner.Id);
        secondApp.DeploymentTargets.Add(
            CreateTarget(secondApp.Id, "Server US", "server-us", runner.Id));
        await db.SaveChangesAsync();

        Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await CreateResolver(db, userId)
                .ResolveAsync(firstApp.Id, "server-us", userId));
    }

    [Test]
    public async Task ResolveAsync_WhenRunnerIsInaccessible_Throws()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(db, Guid.NewGuid(), online: true);
        var app = AddApp(db, userId, runner.Id);
        await db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await CreateResolver(db, userId)
                .ResolveAsync(app.Id, null, userId));

        Assert.That(exception!.Message, Does.Contain("access"));
    }

    [Test]
    public async Task ResolveAsync_WhenOrdinaryAppUsesInfrastructureRunner_Throws()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(
            db,
            userId,
            online: true,
            purpose: RunnerPurposes.Infrastructure);
        var app = AddApp(db, userId, runner.Id);
        app.Tags = "infrastructure,admin";
        await db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await CreateResolver(db, userId)
                .ResolveAsync(app.Id, null, userId));

        Assert.That(exception!.Message, Does.Contain("trusted infrastructure App"));
    }

    [Test]
    public async Task ResolveAsync_WhenTrustedAppAndAdministratorUseInfrastructureRunner_ReturnsTarget()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        AddAdministratorRole(db, userId);
        var runner = AddRunner(
            db,
            userId,
            online: true,
            purpose: RunnerPurposes.Infrastructure);
        var app = AddApp(
            db,
            userId,
            runner.Id,
            InfrastructureAppTrust.PlatformAppId);
        await db.SaveChangesAsync();

        var target = await CreateResolver(db, userId, isAdministrator: true)
            .ResolveAsync(app.Id, null, userId);

        Assert.That(target.ExecutionRunner?.Id, Is.EqualTo(runner.Id));
    }

    [Test]
    public async Task ResolveAsync_WhenTargetIsUnassigned_Throws()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var app = AddApp(db, userId, runnerId: null);
        await db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<AppDeploymentTargetUnassignedException>(
            async () => await CreateResolver(db, userId)
                .ResolveAsync(app.Id, null, userId));

        Assert.That(exception!.Message, Does.Contain("no assigned Runner"));
    }

    [Test]
    public async Task GetAssignedRunnerAsync_WhenRunnerIsOfflineAndOnlineRequired_Throws()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(db, userId, online: false);
        var app = AddApp(db, userId, runner.Id);
        await db.SaveChangesAsync();
        var resolver = CreateResolver(db, userId);
        var authorizationPolicy = CreateRunnerAuthorizationPolicy(db, userId);
        var assignmentPolicy = new AppRunnerAssignmentPolicy(
            db,
            new HostedRunnerAppCapacityService(db),
            authorizationPolicy,
            resolver);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await assignmentPolicy.GetAssignedRunnerAsync(
                app.Id,
                userId,
                requireOnline: true));

        Assert.That(exception!.Message, Does.Contain("unavailable"));
    }

    [Test]
    public async Task AssignAsync_AllowsExplicitPrivilegedHostAssignment()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(
            db,
            userId,
            online: true,
            applicationProfile: RunnerApplicationProfiles.PrivilegedHost);
        var app = AddApp(db, userId, runnerId: null);
        await db.SaveChangesAsync();

        var target = await db.AppDeploymentTargets
            .SingleAsync(x => x.WebAppId == app.Id && x.IsDefault);
        target.PrivilegedHostAccessApproved = true;
        await db.SaveChangesAsync();

        var assignmentPolicy = new AppRunnerAssignmentPolicy(
            db,
            new HostedRunnerAppCapacityService(db),
            CreateRunnerAuthorizationPolicy(db, userId),
            CreateResolver(db, userId));

        await assignmentPolicy.AssignAsync(app, runner.Id, userId);

        Assert.That(
            (await db.AppDeploymentTargets.SingleAsync(x => x.WebAppId == app.Id && x.IsDefault)).ExecutionRunnerId,
            Is.EqualTo(runner.Id));
    }

    private static AppDeploymentTargetResolver CreateResolver(
        ApplicationDbContext db,
        Guid userId,
        bool isAdministrator = false)
    {
        return new AppDeploymentTargetResolver(
            db,
            CreateResourceAuthorizationService(db, userId, isAdministrator),
            CreateRunnerAuthorizationPolicy(db, userId, isAdministrator));
    }

    private static AppRunnerAuthorizationPolicy CreateRunnerAuthorizationPolicy(
        ApplicationDbContext db,
        Guid userId,
        bool isAdministrator = false)
    {
        return new AppRunnerAuthorizationPolicy(
            db,
            CreateResourceAuthorizationService(db, userId, isAdministrator));
    }

    private static ResourceAuthorizationService CreateResourceAuthorizationService(
        ApplicationDbContext db,
        Guid userId,
        bool isAdministrator = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        if (isAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, UserRole.Administrator));
        }

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
        return new ResourceAuthorizationService(
            db,
            new UserContextService(accessor),
            accessor);
    }

    private static WebApp AddApp(
        ApplicationDbContext db,
        Guid userId,
        Guid? runnerId,
        Guid? appId = null)
    {
        var resolvedAppId = appId ?? Guid.NewGuid();
        var app = new WebApp
        {
            Id = resolvedAppId,
            Name = $"App {resolvedAppId:N}",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            DeploymentTargets = { AppDeploymentTarget.CreateDefault(resolvedAppId, runnerId) }
        };
        db.WebApps.Add(app);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = resolvedAppId, UserId = userId });
        return app;
    }

    private static void AddAdministratorRole(ApplicationDbContext db, Guid userId)
    {
        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = UserRole.Administrator,
            NormalizedName = UserRole.Administrator.ToUpperInvariant()
        };
        db.Roles.Add(role);
        db.UserRoles.Add(new ApplicationUserRole
        {
            UserId = userId,
            RoleId = role.Id
        });
    }

    private static Runner AddRunner(
        ApplicationDbContext db,
        Guid userId,
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
                RunnerCapabilities.DefaultsForPurpose(purpose),
                purpose),
            ConnectionId = online ? $"connection-{Guid.NewGuid():N}" : null,
            LastHeartbeatAtUtc = online ? DateTime.UtcNow : null,
            Status = online ? "online" : "offline"
        };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = userId });
        return runner;
    }

    private static AppDeploymentTarget CreateTarget(
        Guid appId,
        string name,
        string slug,
        Guid runnerId)
    {
        var now = DateTimeOffset.UtcNow;
        var target = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = name,
            Slug = slug,
            ExecutionRunnerId = runnerId,
            IsDefault = false,
            Status = DeploymentTargetStatus.NotDeployed,
            CreatedAt = now,
            UpdatedAt = now
        };
        target.SynchronizeDefaultConstraint();
        return target;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"target-resolver-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
