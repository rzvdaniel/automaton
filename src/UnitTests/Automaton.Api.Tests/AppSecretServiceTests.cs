using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class AppSecretServiceTests
{
    [Test]
    public async Task SavedValuesAreDeliveredOnlyInTheirCurrentVersionAndNeverListed()
    {
        var (service, db, appId, ownerId) = Create();

        await service.SetAsync(appId, ownerId, "S3_SECRET_KEY", "first", CancellationToken.None);
        await service.SetAsync(appId, ownerId, "S3_SECRET_KEY", "second", CancellationToken.None);
        await service.SetAsync(appId, ownerId, "S3_BUCKET", "backups", CancellationToken.None);

        var listed = await service.ListAsync(appId, ownerId, CancellationToken.None);
        Assert.That(listed.Select(secret => secret.Name), Is.EqualTo(new[] { "S3_BUCKET", "S3_SECRET_KEY" }));
        Assert.That(listed.Single(secret => secret.Name == "S3_SECRET_KEY").Version, Is.EqualTo(2));
        var resolved = await service.ResolveForExecutionAsync(appId, CancellationToken.None);
        Assert.That(resolved, Is.EqualTo(new Dictionary<string, string> { ["S3_SECRET_KEY"] = "second", ["S3_BUCKET"] = "backups" }));
        Assert.That(await db.SecretVersions.CountAsync(version => version.RevokedAtUtc != null), Is.EqualTo(1));
        Assert.That(await db.SecretVersions.AnyAsync(version => version.ProtectedValue.Contains("second")), Is.False);
    }

    [TestCase("lowercase")]
    [TestCase("1STARTS_WITH_DIGIT")]
    [TestCase("AUTOMATON_APP_ID")]
    [TestCase("HAS-DASH")]
    public void RejectsUnusableEnvironmentVariableNames(string name)
    {
        var (service, _, appId, ownerId) = Create();
        Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync(appId, ownerId, name, "value", CancellationToken.None));
    }

    [Test]
    public async Task DeletedSecretsAreNoLongerDelivered()
    {
        var (service, _, appId, ownerId) = Create();
        await service.SetAsync(appId, ownerId, "TOKEN", "value", CancellationToken.None);

        Assert.That(await service.DeleteAsync(appId, ownerId, "TOKEN", CancellationToken.None), Is.True);
        Assert.That(await service.ResolveForExecutionAsync(appId, CancellationToken.None), Is.Empty);
        Assert.That(await service.DeleteAsync(appId, ownerId, "TOKEN", CancellationToken.None), Is.False);
    }

    [Test]
    public void OnlyUsersWhoCanUpdateTheAppManageItsSecrets()
    {
        var (service, _, appId, _) = Create();
        var stranger = Guid.NewGuid();
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(appId, stranger, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetAsync(appId, stranger, "TOKEN", "value", CancellationToken.None));
    }

    [Test]
    public async Task SecretsOfOneAppAreNotDeliveredToAnother()
    {
        var (service, db, appId, ownerId) = Create();
        var otherApp = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, otherApp, ownerId, "other-app");
        await db.SaveChangesAsync();
        await service.SetAsync(appId, ownerId, "TOKEN", "value", CancellationToken.None);

        Assert.That(await service.ResolveForExecutionAsync(otherApp, CancellationToken.None), Is.Empty);
    }

    private static (AppSecretService Service, ApplicationDbContext Db, Guid AppId, Guid OwnerId) Create()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "secret-app");
        db.SaveChanges();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(ownerId));
        return (new AppSecretService(db, new SecretStore(db, protector), protector, authorization), db, appId, ownerId);
    }
}
