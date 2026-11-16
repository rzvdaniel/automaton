using Automaton.Api.Services;
using Automaton.Api.Enums;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class DeploymentOperationDispatchTests
{
    [Test]
    public async Task PendingOperation_RemainsDispatchableWhenRunnerIsConnectedToPreviousApiColor()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        fixture.Target.ExecutionRunner!.ApiInstanceId = "previous-api-color";
        fixture.Target.ExecutionRunner.ApiColor = "blue";
        await fixture.Db.SaveChangesAsync();
        var operation = await DeploymentOperationTestData.CreateAsync(
            fixture.Db,
            fixture.AppId,
            fixture.OwnerId,
            DeploymentOperationStatuses.Pending,
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "pending-package.zip"));

        var dispatchable = await DeploymentOperationTestScenarios.GetDispatchableOperationIdsAsync(fixture);

        Assert.That(dispatchable, Does.Contain(operation.Id));
    }

    [Test]
    public async Task TimedOutRunningOperation_FailsAndReleasesTarget()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        var (operation, service) = await StuckAsync(fixture, DateTime.UtcNow.AddHours(-1));

        var failed = await service.FailTimedOutOperationsAsync(CancellationToken.None);

        Assert.That(failed, Is.EqualTo(1));
        await AssertFailedAndReleasedAsync(fixture, operation.Id);
    }

    [Test]
    public async Task RecentRunningOperation_IsNotTimedOut()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        var (operation, service) = await StuckAsync(fixture, DateTime.UtcNow.AddMinutes(-1));

        Assert.That(await service.FailTimedOutOperationsAsync(CancellationToken.None), Is.EqualTo(0));
        Assert.That((await fixture.Db.DeploymentOperations.SingleAsync(o => o.Id == operation.Id)).Status,
            Is.EqualTo(DeploymentOperationStatuses.Running));
    }

    [Test]
    public async Task CancelActive_FailsOperationAndLateRunnerResultIsIgnored()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        var (operation, service) = await StuckAsync(fixture, DateTime.UtcNow);

        Assert.That(await service.CancelActiveCoreAsync(fixture.AppId, null, CancellationToken.None), Is.EqualTo(1));
        await AssertFailedAndReleasedAsync(fixture, operation.Id);

        Assert.ThrowsAsync<AppDeploymentTargetConflictException>(() => new AppDeploymentTargetStateService(fixture.Db)
            .MarkCompletedAsync(fixture.AppId, fixture.Target.Id, true));
        Assert.That(await service.CancelActiveCoreAsync(fixture.AppId, null, CancellationToken.None), Is.EqualTo(0));
    }

    [Test]
    public async Task CancelActive_WithTargetSlug_CancelsCliStartedOperationWithoutSlug()
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync();
        var (operation, service) = await StuckAsync(fixture, DateTime.UtcNow);
        Assert.That(operation.TargetSlug, Is.Null);

        Assert.That(await service.CancelActiveCoreAsync(fixture.AppId, "other-target", CancellationToken.None), Is.EqualTo(0));
        Assert.That(await service.CancelActiveCoreAsync(fixture.AppId, fixture.Target.Slug, CancellationToken.None), Is.EqualTo(1));
        await AssertFailedAndReleasedAsync(fixture, operation.Id);
    }

    private static async Task<(DeploymentOperation, DeploymentOperationService)> StuckAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture, DateTime startedAt)
    {
        var attempt = Guid.NewGuid();
        var operation = await DeploymentOperationTestData.CreateAsync(
            fixture.Db, fixture.AppId, fixture.OwnerId, DeploymentOperationStatuses.Running,
            Path.Combine(TestContext.CurrentContext.WorkDirectory, "stuck-package.zip"),
            executionAttemptId: attempt, leaseExpiresAtUtc: DateTime.UtcNow.AddMinutes(5), startedAtUtc: startedAt);
        fixture.Target.Status = DeploymentTargetStatus.Deploying;
        fixture.Target.ActiveDeploymentOperationId = operation.Id;
        fixture.Target.ActiveDeploymentExecutionAttemptId = attempt;
        await fixture.Db.SaveChangesAsync();
        return (operation, DeploymentOperationTestScenarios.CreateService(fixture));
    }

    private static async Task AssertFailedAndReleasedAsync(
        ApplicationDeploymentServiceTests.DeploymentFixture fixture, Guid operationId)
    {
        fixture.Db.ChangeTracker.Clear();
        Assert.That((await fixture.Db.DeploymentOperations.SingleAsync(o => o.Id == operationId)).Status,
            Is.EqualTo(DeploymentOperationStatuses.Failed));
        var target = await fixture.Db.AppDeploymentTargets.SingleAsync(t => t.Id == fixture.Target.Id);
        Assert.That(target.Status, Is.EqualTo(DeploymentTargetStatus.Failed));
        Assert.That(target.ActiveDeploymentOperationId, Is.Null);
    }
}
