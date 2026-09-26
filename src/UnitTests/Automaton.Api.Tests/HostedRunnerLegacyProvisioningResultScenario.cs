using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerLegacyProvisioningResultScenario
{
    internal static async Task VerifyAsync()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var scenario = CreateEntities(db);
        await ApplyLegacyResultAsync(db, scenario);
        await AssertExpectedStateAsync(db, scenario);
    }

    private static Scenario CreateEntities(ApplicationDbContext db)
    {
        var accountId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var runner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = accountId, RunnerHostId = hostId,
            Status = HostedRunnerStatuses.Provisioning
        };
        HostedRunnerAuditTestData.SeedAccountStorageAllocation(db, accountId, 1234567, hostId);
        return new Scenario(accountId, runner, new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = runner.Id,
            Status = HostedRunnerDeploymentStatuses.Executing
        });
    }

    private static async Task ApplyLegacyResultAsync(ApplicationDbContext db, Scenario scenario)
    {
        var handler = new InfrastructureActionProvisioningHandler(
            db, new HostedRunnerAuditNoOpAuditEventService(), Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()));
        using var result = JsonDocument.Parse("{\"success\":true,\"containerId\":\"legacy-id\",\"containerName\":\"legacy\"}");
        await handler.ApplySuccessfulOperationAsync(
            scenario.Runner, scenario.Deployment, "reprovision-hosted-runner", result.RootElement, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private static async Task AssertExpectedStateAsync(ApplicationDbContext db, Scenario scenario)
    {
        var verification = await db.HostedRunnerStorageVerifications.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(scenario.Runner.ContainerId, Is.EqualTo("legacy-id"));
            Assert.That(scenario.Deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForRegistration));
            Assert.That(scenario.Deployment.Logs, Does.Contain("Legacy Infrastructure Action result accepted"));
            Assert.That(verification.Status, Is.EqualTo(StorageVerificationStatuses.Pending));
            Assert.That(verification.Reason, Is.EqualTo("LegacyProvisioningResult"));
            Assert.That(HostedRunnerAuditTestData.GetAccountStorageFilesystemUuid(db, scenario.AccountId), Is.Null);
        });
    }

    private sealed record Scenario(Guid AccountId, HostedRunner Runner, HostedRunnerDeployment Deployment);
}
