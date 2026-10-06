using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal sealed class AppDeletionSupersededExecutorScenario(AppDeletionRecoveryFixture fixture)
{
    private const string BusyMessage = "Target is busy.";
    private readonly Guid appId = fixture.Context.App.Id;
    private readonly Guid originalClaimId = fixture.Context.App.DeletionClaimId!.Value;
    private AppDeploymentTarget? target;
    private AppDeletionExecutionLock? executorA;
    private AppDeletionExecutionLock? executorB;
    private (DeploymentTargetStatus Status, Guid? OperationId, Guid? AttemptId, DateTimeOffset UpdatedAt)? replacementSnapshot;
    private int ownerAssertions;

    internal async Task RunAsync()
    {
        target = await fixture.Context.Db.AppDeploymentTargets.SingleAsync(t => t.WebAppId == appId);
        executorA = await AppDeletionExecutionLock.AcquireAsync(fixture.Context.Db, appId);
        try
        {
            await Assert.ThatAsync(
                () => fixture.Context.Service.ClaimAndCleanupAppRuntimesAsync(
                    appId,
                    recoverInterrupted: true,
                    assertDeletionOwner: AssertOwnerAsync,
                    deletionClaimId: originalClaimId),
                Throws.TypeOf<AppDeploymentTargetConflictException>());

            Assert.That(replacementSnapshot, Is.Not.Null);
            await AssertTargetMatchesAsync(replacementSnapshot!.Value);
            await SimulateOuterDeletionCatchAsync();
            await AssertTargetMatchesAsync(replacementSnapshot.Value);
        }
        finally
        {
            if (executorB is not null) await executorB.DisposeAsync();
            await executorA.DisposeAsync();
        }
    }

    private async Task AssertOwnerAsync(CancellationToken token)
    {
        var claims = new AppDeletionClaimService(fixture.Context.Db);
        if (++ownerAssertions == 1)
        {
            await claims.AssertOwnedAsync(executorA!, originalClaimId, token);
            return;
        }

        await executorA!.DisposeAsync(); // A loses its session.
        executorB = await AppDeletionExecutionLock.AcquireAsync(fixture.Context.Db, appId, token);
        var replacementClaimId = await claims.RecoverAsync(executorB, token);
        var targetState = new AppDeploymentTargetStateService(fixture.Context.Db);
        var recoveredStatus = await targetState.ClaimMutationAsync(
            appId, target!.Id, BusyMessage, token, recoverInterruptedDeletion: true);
        Assert.That(recoveredStatus, Is.EqualTo(DeploymentTargetStatus.Failed));
        replacementSnapshot = await ReadTargetStateAsync(token);
        Assert.That(await ReadAppClaimAsync(token), Is.EqualTo(replacementClaimId));
        await claims.AssertOwnedAsync(executorA, originalClaimId, token);
    }

    private async Task SimulateOuterDeletionCatchAsync()
    {
        // The outer catch can retry release after the inner management catch.
        await fixture.Context.Service.ReleaseAppDeletionClaimsAsync(
            appId,
            new Dictionary<Guid, DeploymentTargetStatus> { [target!.Id] = DeploymentTargetStatus.Failed },
            deletionClaimId: originalClaimId);
    }

    private async Task AssertTargetMatchesAsync(
        (DeploymentTargetStatus Status, Guid? OperationId, Guid? AttemptId, DateTimeOffset UpdatedAt) expected)
    {
        Assert.That(await ReadTargetStateAsync(CancellationToken.None), Is.EqualTo(expected));
    }

    private async Task<(DeploymentTargetStatus, Guid?, Guid?, DateTimeOffset)> ReadTargetStateAsync(
        CancellationToken token) => await fixture.Context.Db.AppDeploymentTargets.AsNoTracking()
        .Where(t => t.Id == target!.Id)
        .Select(t => new ValueTuple<DeploymentTargetStatus, Guid?, Guid?, DateTimeOffset>(
            t.Status, t.ActiveDeploymentOperationId, t.ActiveDeploymentExecutionAttemptId, t.UpdatedAt))
        .SingleAsync(token);

    private Task<Guid?> ReadAppClaimAsync(CancellationToken token) => fixture.Context.Db.WebApps.AsNoTracking()
        .Where(app => app.Id == appId).Select(app => app.DeletionClaimId).SingleAsync(token);
}
