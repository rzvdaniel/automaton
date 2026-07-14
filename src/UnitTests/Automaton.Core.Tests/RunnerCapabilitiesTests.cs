using Automaton.Core.Enums;

namespace Automaton.Core.Tests;

public class RunnerCapabilitiesTests
{
    [Test]
    public void DefaultsForPurpose_IncludeHostManagementForInfrastructure()
    {
        var capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure);

        Assert.That(capabilities, Does.Contain(RunnerCapabilities.HostExecution));
        Assert.That(capabilities, Does.Contain(RunnerCapabilities.HostedRunnerProvisioning));
    }

    [Test]
    public void DefaultsForPurpose_KeepManagedRunnerRestricted()
    {
        var capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed);

        Assert.That(capabilities, Does.Contain(RunnerCapabilities.WorkflowExecution));
        Assert.That(capabilities, Does.Not.Contain(RunnerCapabilities.HostExecution));
    }

    [Test]
    public void Deserialize_InvalidJsonFallsBackToPurposeDefaults()
    {
        var capabilities = RunnerCapabilities.Deserialize("not-json", RunnerPurposes.Infrastructure);

        Assert.That(capabilities, Does.Contain(RunnerCapabilities.HostExecution));
    }

    [Test]
    public void RequiresAdministrator_OnlyMatchesPrivilegedCapabilities()
    {
        Assert.That(
            RunnerCapabilities.RequiresAdministrator(
                [RunnerCapabilities.WorkflowExecution]),
            Is.False);
        Assert.That(
            RunnerCapabilities.RequiresAdministrator(
                [RunnerCapabilities.DockerManagement]),
            Is.True);
    }
}
