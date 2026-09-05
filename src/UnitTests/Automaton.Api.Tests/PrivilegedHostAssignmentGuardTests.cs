using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PrivilegedHostAssignmentGuardTests
{
    [Test]
    public void StandardRunner_does_not_require_approval()
    {
        Assert.DoesNotThrow(() => PrivilegedHostAssignmentGuard.EnsureApproved(
            new AppDeploymentTarget(),
            new Runner { Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.Standard }));
    }

    [Test]
    public void PrivilegedHost_requires_persisted_approval()
    {
        var target = new AppDeploymentTarget();
        var runner = new Runner { Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost };
        var exception = Assert.Throws<InvalidOperationException>(() => PrivilegedHostAssignmentGuard.EnsureApproved(target, runner));
        Assert.That(exception!.Message, Does.Contain("persisted Privileged Host access approval"));
        target.PrivilegedHostAccessApproved = true;
        Assert.DoesNotThrow(() => PrivilegedHostAssignmentGuard.EnsureApproved(target, runner));
    }
}
