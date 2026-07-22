using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class LocalRunnerHostBootstrapServiceTests
{
    [Test]
    public async Task SeedAsync_CreatesLocalRunnerHost_WhenAutoCreationIsEnabled()
    {
        await using var dbContext = CreateDbContext();
        var service = new LocalRunnerHostBootstrapService(
            dbContext,
            new TestOptionsSnapshot<HostedRunnerOptions>(new HostedRunnerOptions
            {
                Enabled = true,
                AutoCreateLocalRunnerHost = true,
                LocalRunnerHostName = "runtime-local-docker",
                LocalRunnerHostCpuCores = 12m,
                LocalRunnerHostMemoryMb = 24576,
                LocalRunnerHostDiskMb = 131072
            }),
            NullLogger<LocalRunnerHostBootstrapService>.Instance);

        await service.SeedAsync();

        var host = await dbContext.RunnerHosts.SingleAsync();
        Assert.That(host.Provider, Is.EqualTo(RunnerHostProviders.Local));
        Assert.That(host.ProvisioningTransport, Is.EqualTo(RunnerHostProvisioningTransports.LocalDocker));
        Assert.That(host.RuntimeConnectivityMode, Is.EqualTo(RunnerHostRuntimeConnectivityModes.LocalDockerNetwork));
        Assert.That(host.HostedRunnerSchedulingEnabled, Is.True);
        Assert.That(host.Status, Is.EqualTo(RunnerHostStatuses.Active));
        Assert.That(host.Name, Is.EqualTo("runtime-local-docker"));
        Assert.That(host.TotalCpuCores, Is.EqualTo(12m));
        Assert.That(host.TotalMemoryMb, Is.EqualTo(24576));
        Assert.That(host.TotalDiskMb, Is.EqualTo(131072));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class TestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T>
        where T : class
    {
        public T Value { get; } = value;

        public T Get(string? name) => Value;
    }
}
