using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

public sealed class RunnerRemovalConcurrencyTests
{
    [Test]
    public async Task StaleRunnerUpdateCannotClearRemovalIntent()
    {
        await using var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(online: true);
        runner.InstallationId = "sh_0123456789abcdef0123456789abcdef";
        await fixture.Db.SaveChangesAsync();
        await using var replica = new Automaton.Api.Data.ApplicationDbContext(fixture.ContextOptions);
        var stale = await replica.Runners.SingleAsync(item => item.Id == runner.Id);
        await Removal(fixture.Db, fixture.UserId)
            .PrepareAsync(runner.Id, CancellationToken.None);

        stale.Name = "Outdated edit";
        replica.Update(stale);

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => replica.SaveChangesAsync());
    }

    private static RunnerRemovalService Removal(ApplicationDbContext db, Guid owner)
        => new(db, new RunnerDeletionPolicy(db,
            new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(owner))));
}
