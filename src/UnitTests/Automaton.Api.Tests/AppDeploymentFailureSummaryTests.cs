using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppDeploymentFailureSummaryTests
{
    [Test]
    public async Task SummaryShowsLatestFailedDeploymentReasonAndClearsItAfterSuccess()
    {
        var (fixture, targetId) = await CreateFailedDeploymentFixtureAsync();
        await using (fixture)
        {
            var failedSummary = await fixture.Service.GetAsync(fixture.App.Id, targetId, fixture.UserId);
            fixture.Db.DeploymentOperations.Add(CreateOperation(
                fixture.App.Id,
                fixture.UserId,
                DeploymentOperationStatuses.Succeeded,
                DateTime.UtcNow.AddSeconds(1),
                null));
            await fixture.Db.SaveChangesAsync();
            var recoveredSummary = await fixture.Service.GetAsync(fixture.App.Id, targetId, fixture.UserId);

            Assert.Multiple(() =>
            {
                Assert.That(failedSummary.LastDeploymentError, Does.Contain("1024 MB"));
                Assert.That(recoveredSummary.LastDeploymentError, Is.Null);
            });
        }
    }

    private static async Task<(
        AppDeploymentTargetManagementServiceTests.TestFixtureContext Fixture,
        Guid TargetId)> CreateFailedDeploymentFixtureAsync()
    {
        var fixture = await AppDeploymentTargetManagementServiceTests.TestFixtureContext.CreateAsync();
        var runner = fixture.AddRunner(online: true);
        await fixture.Db.SaveChangesAsync();
        var target = await fixture.Db.AppDeploymentTargets
            .SingleAsync(candidate => candidate.WebAppId == fixture.App.Id && candidate.IsDefault);
        target.ExecutionRunnerId = runner.Id;
        target.Status = DeploymentTargetStatus.Failed;
        fixture.Db.DeploymentOperations.Add(CreateOperation(
            fixture.App.Id,
            fixture.UserId,
            DeploymentOperationStatuses.Failed,
            DateTime.UtcNow,
            "Machine storage is below the configured 1024 MB free-space reserve."));
        await fixture.Db.SaveChangesAsync();
        return (fixture, target.Id);
    }

    private static DeploymentOperation CreateOperation(
        Guid appId,
        Guid userId,
        string status,
        DateTime createdAtUtc,
        string? errorMessage) => new()
    {
        Id = Guid.NewGuid(),
        WebAppId = appId,
        OwnerUserId = userId,
        Status = status,
        PackagePath = "deployment.zip",
        CreatedAtUtc = createdAtUtc,
        ErrorMessage = errorMessage
    };
}
