using Automaton.Core.Enums;

namespace Automaton.Core.Tests;

public class RunnerPurposesTests
{
    [Test]
    public void NormalizeAcceptsOnlyManagedPurpose()
    {
        Assert.That(RunnerPurposes.Normalize("managed"), Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(() => RunnerPurposes.Normalize("Infrastructure"), Throws.ArgumentException);
        Assert.That(() => RunnerPurposes.Normalize(null), Throws.ArgumentException);
    }

    [Test]
    public void MachineManagementIsTheOnlyApplicationProfile()
    {
        Assert.That(RunnerProfileRules.IsValid(RunnerPurposes.Managed, RunnerApplicationProfiles.MachineManagement), Is.True);
        Assert.That(RunnerProfileRules.IsValid(RunnerPurposes.Managed, "Standard"), Is.False);
        Assert.That(RunnerProfileRules.IsValid(RunnerPurposes.Managed, "PrivilegedHost"), Is.False);
        Assert.That(RunnerProfileRules.IsValid("Infrastructure", RunnerApplicationProfiles.MachineManagement), Is.False);
        Assert.That(() => RunnerProfileRules.NormalizeApplicationProfile(RunnerPurposes.Managed, "Standard"), Throws.ArgumentException);
    }

    [Test]
    public void MachineManagementCanRunApplicationsWithSupportedCapabilities()
    {
        var capabilities = new[]
        {
            RunnerCapabilities.ApplicationHosting,
            RunnerCapabilities.ManagedContainerExecution
        };

        Assert.That(RunnerProfileRules.CanExecuteApplications(
            RunnerPurposes.Managed, RunnerApplicationProfiles.MachineManagement, capabilities), Is.True);
        Assert.That(RunnerProfileRules.CanExecuteApplications(
            RunnerPurposes.Managed, "Standard", capabilities), Is.False);
    }
}
