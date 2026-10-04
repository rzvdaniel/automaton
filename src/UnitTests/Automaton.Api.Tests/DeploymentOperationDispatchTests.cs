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
}
