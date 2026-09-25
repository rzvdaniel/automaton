using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace Automaton.Api.Tests;

[TestFixture]
public class HostedRunnerAdminPlanAuditTests
{

    [Test]
    public async Task PrepareProvisioningAsync_DoesNotMoveRunningRunnerWhenPlanSpecificationChanges()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var scenario = HostedRunnerPlanChangeTestScenario.Seed(db, userId);

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions { Enabled = true, RemoteRuntimePortStart = 20001 });

        var prepared = await service.PrepareProvisioningAsync(userId, Guid.Empty);

        await HostedRunnerPlanChangeTestScenario.AssertRunningRunnerRemainsUnchangedAsync(db, prepared, scenario);
    }
}
