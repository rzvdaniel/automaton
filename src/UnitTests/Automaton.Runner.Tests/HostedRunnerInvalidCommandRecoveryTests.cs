using Automaton.Runner.Console.Services;
using Automaton.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

[TestFixture]
public sealed class HostedRunnerInvalidCommandRecoveryTests
{
    [Test]
    public void GetCommandIdentity_UsesRabbitMetadataAndFallsBackToReadableBody()
    {
        var operationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var command = new HostedRunnerDeploymentCommand(operationId, attemptId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "https://api.test/package", 1, "hash", false, DateTime.UtcNow);

        var metadataIdentity = HostedRunnerInvalidCommandRecovery.GetCommandIdentity(
            operationId.ToString("N"), attemptId.ToString("N"), null);
        var bodyIdentity = HostedRunnerInvalidCommandRecovery.GetCommandIdentity(null, null, command);

        Assert.Multiple(() =>
        {
            Assert.That(metadataIdentity.OperationId, Is.EqualTo(operationId));
            Assert.That(metadataIdentity.AttemptId, Is.EqualTo(attemptId));
            Assert.That(bodyIdentity.OperationId, Is.EqualTo(operationId));
            Assert.That(bodyIdentity.AttemptId, Is.EqualTo(attemptId));
        });
    }

    [Test]
    public async Task InvalidCommand_IsReportedAndRejectedWithoutRequeueBeforeNextDelivery()
    {
        var recovery = new HostedRunnerInvalidCommandRecovery(NullLogger<HostedRunnerInvalidCommandRecovery>.Instance);
        var operationId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var reported = false;
        var requeued = true;
        var nextDeliveryCanRun = false;

        await recovery.RejectAsync(operationId, attemptId, 17, new InvalidDataException("Invalid identity."),
            (reportedOperation, reportedAttempt, reason, _) =>
            {
                reported = reportedOperation == operationId && reportedAttempt == attemptId
                    && reason == "Invalid identity.";
                return Task.FromResult("accepted");
            },
            (requeue, _) =>
            {
                requeued = requeue;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        if (!requeued)
            nextDeliveryCanRun = true;

        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.True);
            Assert.That(requeued, Is.False);
            Assert.That(nextDeliveryCanRun, Is.True);
        });
    }

    [Test]
    public async Task InvalidCommandWithoutIdentity_IsDiscardedWithoutRetry()
    {
        var recovery = new HostedRunnerInvalidCommandRecovery(NullLogger<HostedRunnerInvalidCommandRecovery>.Instance);
        var reportCalled = false;
        bool? requeued = null;

        await recovery.RejectAsync(null, null, 18, new InvalidDataException("Unreadable command."),
            (_, _, _, _) =>
            {
                reportCalled = true;
                return Task.FromResult("accepted");
            },
            (shouldRequeue, _) =>
            {
                requeued = shouldRequeue;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(reportCalled, Is.False);
            Assert.That(requeued, Is.False);
        });
    }

    [Test]
    public async Task InvalidCommand_WhenFailureCannotBePersisted_IsRetriedForRecovery()
    {
        var recovery = new HostedRunnerInvalidCommandRecovery(NullLogger<HostedRunnerInvalidCommandRecovery>.Instance);
        bool? requeued = null;

        await recovery.RejectAsync(Guid.NewGuid(), Guid.NewGuid(), 19,
            new InvalidDataException("Invalid identity."),
            (_, _, _, _) => throw new HttpRequestException("API unavailable."),
            (shouldRequeue, _) =>
            {
                requeued = shouldRequeue;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.That(requeued, Is.True);
    }
}
