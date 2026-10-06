using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeletionRecoveryTests
{
    [Test]
    public async Task Recovery_ResumesInterruptedAppAndTargetCleanup()
    {
        await using var fixture = await AppDeletionRecoveryFixture.CreateAsync();
        fixture.ProbeAdmissionDuringCleanup();
        await fixture.DeleteAsync(recover: true);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Context.Commands.Requests.Select(r => r.DeleteApplicationStorage), Is.EqualTo(new[] { false, true }));
            Assert.That(fixture.Context.Db.WebApps.Any(a => a.Id == fixture.Context.App.Id), Is.False);
        });
    }

    [Test]
    public async Task OrdinaryDelete_DoesNotSilentlySupersedeAnInterruptedClaim()
    {
        await using var fixture = await AppDeletionRecoveryFixture.CreateAsync();
        await Assert.ThatAsync(() => fixture.DeleteAsync(recover: false), Throws.TypeOf<AppDeploymentTargetConflictException>());
        Assert.That(fixture.Context.Commands.Requests, Is.Empty);
    }

    [Test]
    public async Task Recovery_RejectsLiveDeletionExecutor()
    {
        await using var fixture = await AppDeletionRecoveryFixture.CreateAsync();
        await using var owner = await AppDeletionExecutionLock.AcquireAsync(fixture.Context.Db, fixture.Context.App.Id);
        await Assert.ThatAsync(() => fixture.DeleteAsync(recover: true), Throws.TypeOf<AppDeploymentTargetConflictException>());
        Assert.That(fixture.Context.Commands.Requests, Is.Empty);
    }

    [Test]
    public async Task Recovery_FailureKeepsAdmissionFencedAndAllowsSuccessfulRetry()
    {
        await using var fixture = await AppDeletionRecoveryFixture.CreateAsync();
        fixture.Context.Commands.FailStorageCleanup = true;
        await Assert.ThatAsync(() => fixture.DeleteAsync(recover: true), Throws.TypeOf<AppDeploymentTargetConflictException>());
        var claims = new AppDeletionClaimService(fixture.Context.Db);
        await Assert.ThatAsync(() => claims.OpenAdmissionAsync(fixture.Context.App.Id, CancellationToken.None),
            Throws.TypeOf<AppDeploymentTargetConflictException>());
        fixture.Context.Commands.FailStorageCleanup = false;
        await fixture.DeleteAsync(recover: true);
        Assert.That(await fixture.Context.Db.WebApps.AnyAsync(a => a.Id == fixture.Context.App.Id), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Recovery_DoesNotClearAnActiveDeploymentOperation(bool executionAttempt)
    {
        await using var fixture = await AppDeletionRecoveryFixture.CreateAsync();
        var target = await fixture.Context.Db.AppDeploymentTargets.SingleAsync(t => t.WebAppId == fixture.Context.App.Id);
        if (executionAttempt) target.ActiveDeploymentExecutionAttemptId = Guid.NewGuid();
        else target.ActiveDeploymentOperationId = Guid.NewGuid();
        await fixture.Context.Db.SaveChangesAsync();
        await Assert.ThatAsync(() => fixture.DeleteAsync(recover: true), Throws.TypeOf<AppDeploymentTargetConflictException>());
        Assert.That(executionAttempt ? target.ActiveDeploymentExecutionAttemptId : target.ActiveDeploymentOperationId, Is.Not.Null);
        Assert.That(fixture.Context.Commands.Requests, Is.Empty);
    }

    [Test]
    public async Task RecoveryFlagWithoutInterruptedClaim_UsesOrdinaryDeploymentGuard()
    {
        await using var fixture = await AppDeletionRecoveryFixture.CreateAsync();
        fixture.Context.App.DeletionClaimId = null;
        await fixture.Context.Db.SaveChangesAsync();
        await Assert.ThatAsync(() => fixture.DeleteAsync(recover: true), Throws.TypeOf<AppDeploymentTargetConflictException>());
        Assert.That(fixture.Context.Commands.Requests, Is.Empty);
        Assert.That(await fixture.Context.Db.WebApps.AsNoTracking()
            .Where(a => a.Id == fixture.Context.App.Id).Select(a => a.DeletionClaimId).SingleAsync(), Is.Null);
    }

    [Test]
    public async Task Recovery_OlderClaimOwnerCannotReleaseReplacementFence()
    {
        await using var fixture = await AppDeletionRecoveryFixture.CreateAsync();
        var old = fixture.Context.App.DeletionClaimId!.Value;
        await using var executor = await AppDeletionExecutionLock.AcquireAsync(fixture.Context.Db, fixture.Context.App.Id);
        var claims = new AppDeletionClaimService(fixture.Context.Db);
        var current = await claims.RecoverAsync(executor);
        await claims.ReleaseAsync(fixture.Context.App.Id, old);
        await claims.AssertOwnedAsync(executor, current, CancellationToken.None);
        await executor.DisposeAsync();
        await Assert.ThatAsync(() => executor.AssertOwnedAsync(), Throws.TypeOf<AppDeploymentTargetConflictException>());
    }
}
