using Automaton.Core.Enums;

namespace Automaton.Core.Tests;

public class RunnerCapabilitiesTests
{
    [Test]
    public void DefaultsForManagedRunnerContainOnlySupportedCapabilities()
    {
        var capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed);

        Assert.That(capabilities, Is.EquivalentTo(new[]
        {
            RunnerCapabilities.ApplicationHosting,
            RunnerCapabilities.WorkflowExecution,
            RunnerCapabilities.ManagedContainerExecution
        }));
    }

    [Test]
    public void NormalizeDropsRetiredCapabilities()
    {
        var capabilities = RunnerCapabilities.Normalize(
            [RunnerCapabilities.ApplicationHosting, "HostExecution", "HostContainerExecution"],
            RunnerPurposes.Managed);

        Assert.That(capabilities, Is.EquivalentTo(new[] { RunnerCapabilities.ApplicationHosting }));
    }

    [Test]
    public void DeserializeInvalidJsonFallsBackToManagedDefaults()
    {
        var capabilities = RunnerCapabilities.Deserialize("not-json", RunnerPurposes.Managed);

        Assert.That(capabilities, Is.EquivalentTo(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed)));
    }

    [Test]
    public void LegacyPurposeCannotExecuteApplications()
    {
        Assert.That(RunnerCapabilities.Normalize([RunnerCapabilities.ApplicationHosting], "Infrastructure"), Is.Empty);
        Assert.That(RunnerCapabilities.CanExecuteApplications([RunnerCapabilities.ApplicationHosting], "Infrastructure"), Is.False);
    }

    [Test]
    public void ApplicationExecutionRequiresApplicationHosting()
    {
        Assert.That(RunnerCapabilities.CanExecuteApplications(
            [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution], RunnerPurposes.Managed), Is.True);
        Assert.That(RunnerCapabilities.CanExecuteApplications(
            [RunnerCapabilities.WorkflowExecution], RunnerPurposes.Managed), Is.False);
    }
}
