using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppDeploymentTargetResolverTests
{
    [Test]
    public async Task ResolveAsync_WithUnknownSlug_Throws()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var runner = AddRunner(db, userId, online: true);
        var app = AddApp(db, userId, runner.Id);
        await db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await AppDeploymentTargetResolverTestServices.CreateResolver(db, userId)
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
            async () => await AppDeploymentTargetResolverTestServices.CreateResolver(db, userId)
                .ResolveAsync(firstApp.Id, "server-us", userId));
    }

    [Test]
    public async Task ResolveAsync_WhenTargetIsUnassigned_Throws()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var app = AddApp(db, userId, runnerId: null);
        await db.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<AppDeploymentTargetUnassignedException>(
            async () => await AppDeploymentTargetResolverTestServices.CreateResolver(db, userId)
                .ResolveAsync(app.Id, null, userId));

        Assert.That(exception!.Message, Does.Contain("no assigned Runner"));
        Assert.That(exception.Message, Does.Contain("Assign a Management Runner"));
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
