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
public class HostedRunnerDeleteLifecycleTests
{


    [Test]
    public async Task SuccessfulHostedRunnerDeleteReleasesRuntimePort()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "delete-port",
            ContainerName = "delete-port", RuntimePort = 20000, Status = HostedRunnerStatuses.Deleting
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Deprovision,
            Status = HostedRunnerDeploymentStatuses.Executing
        };
        var handler = new InfrastructureActionProvisioningHandler(
            db, new HostedRunnerAuditNoOpAuditEventService(), Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()));
        using var document = JsonDocument.Parse("{\"success\":true}");

        await handler.ApplySuccessfulOperationAsync(
            hostedRunner, deployment, "delete-hosted-runner", document.RootElement, CancellationToken.None);

        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Deleted));
        Assert.That(hostedRunner.RuntimePort, Is.Null);
    }
}
