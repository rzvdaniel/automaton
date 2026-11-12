using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class AppSettingValueServiceTests
{
    [Test]
    public async Task SavedSettingIsReturnedAndStoredOutsideTheSecretStoreDeletingTheOldOverride()
    {
        var (settings, secrets, db, appId, ownerId) = Create("""{"settings":[{"name":"S3_ENDPOINT"}]}""");
        await secrets.SetAsync(appId, ownerId, "S3_ENDPOINT", "https://old.example", CancellationToken.None);

        await settings.SetAsync(appId, ownerId, "S3_ENDPOINT", "https://r2.example", CancellationToken.None);

        var listed = await settings.ListAsync(appId, ownerId, CancellationToken.None);
        Assert.That(listed.Select(item => (item.Name, item.Value)), Is.EqualTo(new[] { ("S3_ENDPOINT", "https://r2.example") }));
        Assert.That(await secrets.ListAsync(appId, ownerId, CancellationToken.None), Is.Empty);
        Assert.That(await db.AppSettingValues.CountAsync(), Is.EqualTo(1));
        await settings.SetAsync(appId, ownerId, "S3_ENDPOINT", "https://second.example", CancellationToken.None);
        Assert.That((await settings.ListAsync(appId, ownerId, CancellationToken.None)).Single().Value, Is.EqualTo("https://second.example"));
    }

    [Test]
    public async Task ASecretIsNeverReturnedEvenWhenTheContractRedeclaresItAsASetting()
    {
        var (settings, secrets, db, appId, ownerId) = Create("""{"secrets":[{"name":"S3_KEY"}]}""");
        await secrets.SetAsync(appId, ownerId, "S3_KEY", "hunter2", CancellationToken.None);
        var app = await db.WebApps.SingleAsync(app => app.Id == appId);
        app.ApplicationContractJson = """{"settings":[{"name":"S3_KEY"}]}""";
        await db.SaveChangesAsync();

        Assert.That(await settings.ListAsync(appId, ownerId, CancellationToken.None), Is.Empty);
        app.ApplicationContractJson = """{"settings":[{"name":"S3_KEY"}],"secrets":[{"name":"S3_KEY"}]}""";
        await db.SaveChangesAsync();
        Assert.ThrowsAsync<ArgumentException>(() => settings.SetAsync(appId, ownerId, "S3_KEY", "x", CancellationToken.None));
        Assert.That((await secrets.ResolveForExecutionAsync(appId, CancellationToken.None))["S3_KEY"], Is.EqualTo("hunter2"));
    }

    [Test]
    public async Task UndeclaredNamesAndStrangersAreRefused()
    {
        var (settings, _, _, appId, ownerId) = Create("""{"settings":[{"name":"REGION"}]}""");
        Assert.ThrowsAsync<ArgumentException>(() => settings.SetAsync(appId, ownerId, "OTHER", "x", CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => settings.ListAsync(appId, Guid.NewGuid(), CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => settings.SetAsync(appId, Guid.NewGuid(), "REGION", "x", CancellationToken.None));
    }

    [Test]
    public async Task TheEnvironmentPrefersTheSettingsTableThenTheSecretStoreThenTheDefault()
    {
        var (settings, secrets, _, appId, ownerId) = Create(
            """{"settings":[{"name":"A","default":"da"},{"name":"B","default":"db"},{"name":"C","default":"dc"}]}""");
        await secrets.SetAsync(appId, ownerId, "A", "old-a", CancellationToken.None);
        await secrets.SetAsync(appId, ownerId, "B", "old-b", CancellationToken.None);
        await settings.SetAsync(appId, ownerId, "A", "new-a", CancellationToken.None);
        // A secret-store override written after the settings value (for example by the CLI) must not beat it.
        await secrets.SetAsync(appId, ownerId, "A", "cli-a", CancellationToken.None);

        var env = await secrets.ResolveForExecutionAsync(appId, CancellationToken.None);

        Assert.That(env["A"], Is.EqualTo("new-a"));
        Assert.That(env["B"], Is.EqualTo("old-b"));
        Assert.That(env["C"], Is.EqualTo("dc"));
        var dispatch = await secrets.ResolveForDispatchAsync(appId, CancellationToken.None);
        Assert.That(dispatch.Delivered.Select(entry => entry.Name), Does.Not.Contain("A"));
    }

    [Test]
    public async Task ClearingGoesBackToTheDefaultAndRemovesOldOverrides()
    {
        var (settings, secrets, _, appId, ownerId) = Create("""{"settings":[{"name":"A","default":"da"}]}""");
        await secrets.SetAsync(appId, ownerId, "A", "old", CancellationToken.None);
        await settings.SetAsync(appId, ownerId, "A", "new", CancellationToken.None);

        Assert.That(await settings.ClearAsync(appId, ownerId, "A", CancellationToken.None), Is.True);

        Assert.That((await secrets.ResolveForExecutionAsync(appId, CancellationToken.None))["A"], Is.EqualTo("da"));
    }

    private static (AppSettingValueService Settings, AppSecretService Secrets, ApplicationDbContext Db, Guid AppId, Guid OwnerId) Create(string contractJson)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "settings-app");
        db.WebApps.Single(app => app.Id == appId).ApplicationContractJson = contractJson;
        db.SaveChanges();
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var authorization = new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(ownerId));
        var secrets = new AppSecretService(db, new SecretStore(db, protector), protector, authorization);
        return (new AppSettingValueService(db, secrets, authorization), secrets, db, appId, ownerId);
    }
}
