using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Worker.Services;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class HostedRunnerStorageVerificationPriorityTests
{
    [Test]
    public void AdministratorRequestedVerification_OnStaleRunnerRetainsInteractivePriority()
    {
        var verification = new HostedRunnerStorageVerification
        {
            Reason = "AdministratorRequested",
            HostedRunner = new HostedRunner
            {
                StorageState = HostedRunnerStorageStates.Stale,
                StorageMeasurementStatus = StorageMeasurementStatuses.Stale
            }
        };
        Assert.That(HostedRunnerStorageReconciliationService.VerificationPriority(verification), Is.EqualTo(4));
    }

    [Test]
    public void CriticalStorageStateRemainsAheadOfAdministratorRequest()
    {
        var verification = new HostedRunnerStorageVerification
        {
            Reason = "AdministratorRequested",
            HostedRunner = new HostedRunner { StorageState = HostedRunnerStorageStates.OverLimit }
        };
        Assert.That(HostedRunnerStorageReconciliationService.VerificationPriority(verification), Is.LessThan(4));
    }
}
