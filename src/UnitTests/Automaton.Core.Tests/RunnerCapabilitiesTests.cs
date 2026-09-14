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

    [Test]
    public void CanExecuteApplications_RequiresManagedApplicationHostingWithoutPrivilegedCapabilities()
    {
        Assert.That(
            RunnerCapabilities.CanExecuteApplications(
                [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution],
                RunnerPurposes.Managed),
            Is.True);
        Assert.That(
            RunnerCapabilities.CanExecuteApplications(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                RunnerPurposes.Infrastructure),
            Is.False);
        Assert.That(
            RunnerCapabilities.CanExecuteApplications(
                [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.DockerManagement],
                RunnerPurposes.Managed),
            Is.False);
    }

    [Test]
    public void ForInstallationProfile_RefreshesHistoricalInfrastructureDefaults()
    {
        var capabilities = RunnerCapabilities.ForInstallationProfile(
            [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution],
            RunnerPurposes.Infrastructure);

        Assert.That(capabilities, Is.EquivalentTo(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure)));
    }

    [Test]
    public void InstallationProfile_UsesCanonicalInfrastructureAndManagedProfiles()
    {
        var canonicalInfrastructure = RunnerCapabilities.ForInstallationProfile(
            [RunnerCapabilities.ApplicationHosting],
            RunnerPurposes.Infrastructure);
        var managed = RunnerCapabilities.ForInstallationProfile(
            [RunnerCapabilities.ApplicationHosting],
            RunnerPurposes.Managed);

        Assert.Multiple(() =>
        {
            Assert.That(canonicalInfrastructure, Does.Contain(RunnerCapabilities.HostContainerExecution));
            Assert.That(managed, Does.Not.Contain(RunnerCapabilities.HostContainerExecution));
        });
    }
}
