using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

[TestFixture, NonParallelizable]
public sealed class RunnerLogSinkSyncTests
{
    private const string Variable = RunnerRuntimeEndpointConfiguration.HostAddressEnvironmentVariable;
    private string? saved;

    [SetUp]
    public void Save() => saved = Environment.GetEnvironmentVariable(Variable);

    [TearDown]
    public void Restore() => Environment.SetEnvironmentVariable(Variable, saved);

    [Test]
    public void Reports_every_running_runtime_with_the_address_the_server_can_reach()
    {
        Environment.SetEnvironmentVariable(Variable, "172.19.0.1");
        var registry = new RunnerRuntimeRegistry();
        var (app, target) = (Guid.NewGuid(), Guid.NewGuid());
        registry.Register(app, target, 32784);

        var reports = RunnerLogSinkSync.Reports(registry.Snapshot());

        Assert.That(reports, Has.Length.EqualTo(1));
        Assert.That(reports[0].AppId, Is.EqualTo(app));
        Assert.That(reports[0].DeploymentTargetId, Is.EqualTo(target));
        Assert.That(reports[0].Address, Is.EqualTo("http://172.19.0.1:32784"));
    }

    [Test]
    public void A_removed_runtime_is_no_longer_reported_and_a_bad_host_address_reports_nothing()
    {
        Environment.SetEnvironmentVariable(Variable, "172.19.0.1");
        var registry = new RunnerRuntimeRegistry();
        var (app, target) = (Guid.NewGuid(), Guid.NewGuid());
        registry.Register(app, target, 32784);
        registry.Remove(app, target);
        Assert.That(RunnerLogSinkSync.Reports(registry.Snapshot()), Is.Empty);

        registry.Register(app, target, 32784);
        Environment.SetEnvironmentVariable(Variable, "not an address");
        Assert.That(RunnerLogSinkSync.Reports(registry.Snapshot()), Is.Empty);
    }
}
