using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppDeploymentTargetModelTests
{
    [Test]
    public async Task SaveChangesAsync_CreatesExactlyOnePrimaryLiveTarget_ForNewApp()
    {
        await using var db = CreateDbContext();
        var app = CreateApp();
        db.WebApps.Add(app);

        await db.SaveChangesAsync();

        var target = await db.AppDeploymentTargets.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(target.WebAppId, Is.EqualTo(app.Id));
            Assert.That(target.Name, Is.EqualTo("Live"));
            Assert.That(target.Slug, Is.EqualTo("live"));
            Assert.That(target.IsDefault, Is.True);
            Assert.That(target.DefaultConstraintKey, Is.EqualTo(1));
            Assert.That(target.ExecutionRunnerId, Is.Null);
            Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Unassigned));
        });
    }

    [Test]
    public void Model_DefinesTargetUniquenessAndDeletionConstraints()
    {
        using var db = CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(AppDeploymentTarget))!;

        var uniqueProperties = entity.GetIndexes()
            .Where(index => index.IsUnique)
            .Select(index => index.Properties.Select(property => property.Name).ToArray())
            .ToArray();
        var foreignKeys = entity.GetForeignKeys().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                uniqueProperties,
                Has.Some.EqualTo(new[] { nameof(AppDeploymentTarget.WebAppId), nameof(AppDeploymentTarget.Slug) }));
            Assert.That(
                uniqueProperties,
                Has.Some.EqualTo(new[] { nameof(AppDeploymentTarget.WebAppId), nameof(AppDeploymentTarget.DefaultConstraintKey) }));
            Assert.That(
                foreignKeys.Single(key => key.PrincipalEntityType.ClrType == typeof(WebApp)).DeleteBehavior,
                Is.EqualTo(DeleteBehavior.Cascade));
            Assert.That(
                foreignKeys.Single(key => key.PrincipalEntityType.ClrType == typeof(Runner)).DeleteBehavior,
                Is.EqualTo(DeleteBehavior.SetNull));
        });
    }

    [Test]
    public async Task RemovingApp_CascadesItsDeploymentTargets()
    {
        await using var db = CreateDbContext();
        var app = CreateApp();
        db.WebApps.Add(app);
        await db.SaveChangesAsync();

        db.WebApps.Remove(app);
        await db.SaveChangesAsync();

        Assert.That(await db.AppDeploymentTargets.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task RemovingOnlyDefaultTarget_IsRejected()
    {
        await using var db = CreateDbContext();
        var app = CreateApp();
        db.WebApps.Add(app);
        await db.SaveChangesAsync();

        db.AppDeploymentTargets.Remove(await db.AppDeploymentTargets.SingleAsync());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await db.SaveChangesAsync());
        Assert.That(
            exception!.Message,
            Does.Contain("exactly one primary deployment target"));
    }

    [Test]
    public void AddingTwoDefaultTargets_IsRejected()
    {
        using var db = CreateDbContext();
        var app = CreateApp();
        app.DeploymentTargets.Add(AppDeploymentTarget.CreateDefault(app.Id));
        app.DeploymentTargets.Add(AppDeploymentTarget.CreateDefault(app.Id));
        db.WebApps.Add(app);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await db.SaveChangesAsync());

        Assert.That(
            exception!.Message,
            Does.Contain("exactly one primary deployment target"));
    }

    private static WebApp CreateApp()
    {
        return new WebApp
        {
            Id = Guid.NewGuid(),
            Name = "Target model test",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"deployment-targets-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
