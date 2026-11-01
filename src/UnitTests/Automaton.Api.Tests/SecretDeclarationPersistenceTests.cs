using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

/// <summary>
/// The Runner loads secrets while it starts a new runtime, before the deployment finalizes the contract,
/// so a deployment stores its secret declarations first and leaves the rest of the contract alone.
/// </summary>
public sealed class SecretDeclarationPersistenceTests
{
    [Test]
    public async Task StoresOnlyTheDeclarationsOfTheIncomingPackage()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, Guid.NewGuid(), "app");
        var app = await db.WebApps.SingleAsync(item => item.Id == appId);
        app.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract { Runtime = { Port = 8080 } });
        await db.SaveChangesAsync();
        var service = new DeploymentPackageFinalizationService(db, null!, null!, null!);

        await service.PersistSecretDeclarationsAsync(appId, new ApplicationContract
        {
            Runtime = { Port = 9090 },
            Secrets = [new() { Name = "STRIPE_KEY" }]
        }, CancellationToken.None);

        var stored = ApplicationContractSerialization.Deserialize((await db.WebApps.SingleAsync(item => item.Id == appId)).ApplicationContractJson);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Secrets.Select(secret => secret.Name), Is.EqualTo(new[] { "STRIPE_KEY" }));
            Assert.That(stored.Runtime.Port, Is.EqualTo(8080), "the rest of the contract changes only when the deployment succeeds");
        });
    }

    [Test]
    public async Task LeavesAnUnreadableStoredContractUntouched()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, Guid.NewGuid(), "app");
        var app = await db.WebApps.SingleAsync(item => item.Id == appId);
        app.ApplicationContractJson = """{"frontend":{}}""";
        await db.SaveChangesAsync();

        await new DeploymentPackageFinalizationService(db, null!, null!, null!).PersistSecretDeclarationsAsync(
            appId, new ApplicationContract { Secrets = [new() { Name = "KEY" }] }, CancellationToken.None);

        Assert.That((await db.WebApps.SingleAsync(item => item.Id == appId)).ApplicationContractJson, Is.EqualTo("""{"frontend":{}}"""));
    }
}
