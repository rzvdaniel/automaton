using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerIdentityPolicyTests
{
    [Test]
    public void ManagedHostedRunner_UsesAuthoritativeEnrollmentIdentity()
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

    [Test]
    public void ManagedHostedRunnerWithoutPersistedIdentity_FailsClosedAtStartupBoundary()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RunnerHostService.EnsureManagedHostedRunnerIdentity("Managed", Guid.NewGuid().ToString()));

        Assert.That(exception!.Message, Does.Contain("identity is unavailable"));
    }

    [Test]
    public void SelfHostedRunner_RemainsEligibleForGenericReconciliation()
    {
        Assert.That(RunnerHostService.ShouldRunGenericIdentityReconciliation("SelfHosted", Guid.NewGuid().ToString()), Is.True);
    }
}
