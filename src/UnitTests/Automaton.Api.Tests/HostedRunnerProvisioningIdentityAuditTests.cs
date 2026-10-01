using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Automaton.Api.Tests;

[TestFixture]
public class HostedRunnerProvisioningIdentityAuditTests
{

    [TestCase("{\"success\":true,\"containerName\":\"replacement\"}")]
    [TestCase("{\"success\":true,\"containerId\":\"replacement-id\",\"containerName\":\"\"}")]
    public void HostedRunnerProvisioningRejectsMissingContainerIdentity(string json)
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var hostedRunner = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Status = HostedRunnerStatuses.Provisioning };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id, Status = HostedRunnerDeploymentStatuses.Executing };
        var handler = RunnerExecutionTestServices.Lifecycle(
            db, new HostedRunnerAuditNoOpAuditEventService(), null!);
        using var document = JsonDocument.Parse(json);

        Assert.ThrowsAsync<InvalidOperationException>(() => handler.ApplySuccessfulOperationAsync(
            hostedRunner, deployment, "reprovision-hosted-runner", document.RootElement, CancellationToken.None));
        Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Executing));
        Assert.That(hostedRunner.ContainerId, Is.Null);
    }


    [Test]
    public async Task HostedRunnerProvisioningWithContainerIdentityWaitsForRegistration()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var hostId = Guid.NewGuid();
        var hostedRunner = new HostedRunner { Id = Guid.NewGuid(), UserId = userId, RunnerHostId = hostId, Status = HostedRunnerStatuses.Provisioning };
        HostedRunnerAuditTestData.SeedAccountStorageAllocation(db, userId, 1234567, hostId);
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id, Status = HostedRunnerDeploymentStatuses.Executing };
        var handler = RunnerExecutionTestServices.Lifecycle(
            db, new HostedRunnerAuditNoOpAuditEventService(), null!);
        using var document = JsonDocument.Parse("{\"success\":true,\"containerId\":\"replacement-id\",\"containerName\":\"replacement\",\"storageFilesystemUuid\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\"}");

        await handler.ApplySuccessfulOperationAsync(
            hostedRunner, deployment, "reprovision-hosted-runner", document.RootElement, CancellationToken.None);

        Assert.That(hostedRunner.ContainerId, Is.EqualTo("replacement-id"));
        Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForRegistration));
        Assert.That(
            HostedRunnerAuditTestData.GetAccountStorageFilesystemUuid(db, userId),
            Is.EqualTo("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
    }
}
