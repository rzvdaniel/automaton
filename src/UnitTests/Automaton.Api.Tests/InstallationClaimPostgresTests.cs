using Automaton.Api.Services;
using Automaton.Tests.Shared;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InstallationClaimPostgresTests
{
    private PostgresTestDatabase database = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase() => database = await PostgresTestDatabase.CreateAsync();

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (database is not null) await database.DisposeAsync();
    }

    [Test]
    public async Task SecondSetupIsRejectedWhileTheFirstClaimIsHeldAndAllowedAfterItEnds()
    {
        await using var first = database.Open();
        await using var second = database.Open();
        var claim = await InstallationClaim.AcquireAsync(first, CancellationToken.None);

        Assert.ThrowsAsync<InstallationSetupException>(() => InstallationClaim.AcquireAsync(second, CancellationToken.None));

        await claim.DisposeAsync();
        await using var retry = await InstallationClaim.AcquireAsync(second, CancellationToken.None);
    }
}
