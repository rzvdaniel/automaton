using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;
using static Automaton.Api.Tests.BackendRouteConfigTestFixture;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRuntimeRetirementTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RefreshAsync_DoesNotRouteRetiredHostedEndpointOrAdoptItsRunner(bool deleted)
    {
        var rootPath = CreateTempDirectory();
        await using var db = CreateDbContext();
        var runnerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        AddMachineRunner(db, runnerId);
        db.HostedRunners.Add(new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "Retired Hosted",
            RunnerId = runnerId, RuntimeEndpoint = "http://retired-hosted:8080",
            ContainerName = "retired-hosted", Status = "Running",
            DeletedAt = deleted ? DateTime.UtcNow : null
        });
        var target = AppDeploymentTarget.CreateDefault(appId, runnerId);
        target.GenericRuntimeDeployed = true;
        AddApp(db, appId, target, GenericRuntimeContractJson());
        await db.SaveChangesAsync();
        var path = Path.Combine(rootPath, "managed-runtime-routes.yml");
        await File.WriteAllTextAsync(path, "previous legacy routes");

        await CreateService(db, rootPath).RefreshAsync();

        Assert.That(File.Exists(path), Is.False);
        Assert.That(await db.HostedRunners.CountAsync(), Is.EqualTo(1));
    }

}
