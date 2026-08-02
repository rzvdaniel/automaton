using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class PrivilegedHostAssignmentGuardTests
{
    [Test]
    public void StandardRunner_does_not_require_acknowledgement()
    {
        Assert.DoesNotThrow(() => PrivilegedHostAssignmentGuard.EnsureAcknowledged(
            new Runner { Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.Standard }, false));
    }

    [Test]
    public void PrivilegedHost_requires_acknowledgement()
    {
        var runner = new Runner { Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.PrivilegedHost };
        var exception = Assert.Throws<InvalidOperationException>(() => PrivilegedHostAssignmentGuard.EnsureAcknowledged(runner, false));
        Assert.That(exception!.Message, Does.Contain("requires explicit host-access acknowledgement"));
        Assert.DoesNotThrow(() => PrivilegedHostAssignmentGuard.EnsureAcknowledged(runner, true));
    }
}
