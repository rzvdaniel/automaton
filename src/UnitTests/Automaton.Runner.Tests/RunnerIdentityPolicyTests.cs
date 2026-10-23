using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerIdentityPolicyTests
{
    [Test]
    public void ManagedHostedRunnerMarker_IsRetired()
    {
        Assert.That(RunnerIdentityPolicy.IsManagedHostedRunner("Managed", Guid.NewGuid().ToString()), Is.True);
    }

    [Test]
    public void ManagedHostedRunnerWithoutIdentity_IsNotGenericRegistrationCandidate()
    {
        Assert.That(RunnerIdentityPolicy.IsManagedHostedRunner("Managed", null), Is.False);
        Assert.That(RunnerIdentityPolicy.IsManagedHostedRunner("Managed", string.Empty), Is.False);
    }

    [Test]
    public void SelfHostedRunner_RemainsOnGenericRegistrationPath()
    {
        Assert.That(RunnerIdentityPolicy.IsManagedHostedRunner("SelfHosted", Guid.NewGuid().ToString()), Is.False);
    }

    [Test]
    public void ManagedHostedRunnerWithPersistedIdentity_SkipsGenericReconciliation()
    {
        Assert.That(RunnerHostService.ShouldRunGenericIdentityReconciliation("Managed", Guid.NewGuid().ToString()), Is.False);
    }

    [TestCase("adcb1b5f-cc81-4a5d-9915-af629bf1c6ab")]
    [TestCase("invalid-retired-marker")]
    public void RetiredHostedRunnerMarker_FailsClosedAtStartupBoundary(string marker)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RunnerHostService.EnsureManagedHostedRunnerIdentity("Managed", marker));

        Assert.That(exception!.Message, Does.Contain("execution is retired"));
    }

    [Test]
    public void SelfHostedRunner_RemainsEligibleForGenericReconciliation()
    {
        Assert.That(RunnerHostService.ShouldRunGenericIdentityReconciliation("SelfHosted", Guid.NewGuid().ToString()), Is.True);
    }

    [Test]
    public void MachineManagementWithoutHostedMarker_IsNotBlocked()
    {
        Assert.DoesNotThrow(() => RunnerHostService.EnsureManagedHostedRunnerIdentity("Managed", null));
        Assert.That(RunnerHostService.ShouldRunGenericIdentityReconciliation("Managed", null), Is.True);
    }

    [TestCase(System.Net.HttpStatusCode.OK, RunnerCredentialValidationResult.Valid)]
    [TestCase(System.Net.HttpStatusCode.Unauthorized, RunnerCredentialValidationResult.Rejected)]
    [TestCase(System.Net.HttpStatusCode.TooManyRequests, RunnerCredentialValidationResult.Unavailable)]
    [TestCase(System.Net.HttpStatusCode.InternalServerError, RunnerCredentialValidationResult.Unavailable)]
    public void CredentialValidation_MapsAuthoritativeAndTransientStatuses(
        System.Net.HttpStatusCode statusCode,
        RunnerCredentialValidationResult expected)
    {
        Assert.That(ApiKeyAuthenticationService.MapCredentialValidationStatus(statusCode), Is.EqualTo(expected));
    }
}
