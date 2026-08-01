using Automaton.Core.Enums;

namespace Automaton.Core.Tests;

public class RunnerPurposesTests
{
    [Test]
    public void Normalize_DefaultsUnknownAndEmptyValuesToManaged()
    {
        Assert.That(RunnerPurposes.Normalize(null), Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(RunnerPurposes.Normalize(string.Empty), Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(RunnerPurposes.Normalize("unknown"), Is.EqualTo(RunnerPurposes.Managed));
    }

    [Test]
    public void Normalize_PreservesInfrastructurePurposeCaseInsensitively()
    {
        Assert.That(RunnerPurposes.Normalize("infrastructure"), Is.EqualTo(RunnerPurposes.Infrastructure));
    }

    [Test]
    public void ApplicationProfile_DefaultsLegacyManagedRunnersToStandard()
    {
        Assert.That(
            RunnerProfileRules.NormalizeApplicationProfile(RunnerPurposes.Managed, null),
            Is.EqualTo(RunnerApplicationProfiles.Standard));
        Assert.That(
            RunnerProfileRules.IsPrivilegedHost(RunnerPurposes.Managed, RunnerApplicationProfiles.PrivilegedHost),
            Is.True);
    }

    [Test]
    public void ApplicationProfile_RejectsUnknownValuesInsteadOfDefaulting()
    {
        Assert.That(RunnerApplicationProfiles.IsValidForPurpose("mystery", RunnerPurposes.Managed), Is.False);
        Assert.That(
            () => RunnerApplicationProfiles.Normalize("mystery"),
            Throws.ArgumentException);
        Assert.That(
            RunnerProfileRules.NormalizeApplicationProfile(RunnerPurposes.Managed, "standard"),
            Is.EqualTo(RunnerApplicationProfiles.Standard));
    }

    [Test]
    public void InfrastructureRunnersCannotHaveAnApplicationProfile()
    {
        Assert.That(
            RunnerProfileRules.IsValid(RunnerPurposes.Infrastructure, RunnerApplicationProfiles.PrivilegedHost),
            Is.False);
        Assert.That(
            RunnerProfileRules.NormalizeApplicationProfile(RunnerPurposes.Infrastructure, null),
            Is.Null);
    }

    [Test]
    public void PrivilegedHostProfileDoesNotGrantInfrastructureCapabilities()
    {
        var capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed);

        Assert.That(capabilities, Does.Not.Contain(RunnerCapabilities.HostedRunnerProvisioning));
        Assert.That(
            RunnerProfileRules.IsPrivilegedHost(RunnerPurposes.Managed, RunnerApplicationProfiles.PrivilegedHost),
            Is.True);
        Assert.That(
            RunnerCapabilities.RequiresAdministrator(capabilities, RunnerPurposes.Managed),
            Is.False);
        Assert.That(
            RunnerProfileRules.CanExecuteApplications(RunnerPurposes.Managed, RunnerApplicationProfiles.PrivilegedHost, capabilities),
            Is.True);
    }
}
