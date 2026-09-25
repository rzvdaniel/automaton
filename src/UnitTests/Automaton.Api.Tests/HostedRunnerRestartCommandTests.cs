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
public class HostedRunnerRestartCommandTests
{


    [Test]
    public async Task RestartCommandSuccessWaitsForReconnectInsteadOfCompletingOperation()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var runnerId = Guid.NewGuid();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), RunnerId = runnerId,
            Name = "restart-command", ContainerName = "restart-command", Status = HostedRunnerStatuses.Running
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Restart,
            Status = HostedRunnerDeploymentStatuses.Executing,
            ExpectedRunnerId = runnerId
        };
        await HostedRunnerRestartLifecycleScenario.ApplySuccessfulRestartCommandAsync(db, hostedRunner, operation);

        Assert.That(operation.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForReconnect));
        Assert.That(operation.FinishedAt, Is.Null);
        Assert.That(operation.ReconnectAfterUtc, Is.Not.Null);
        Assert.That(operation.ReconnectDeadlineUtc, Is.GreaterThan(operation.ReconnectAfterUtc));
        Assert.That(operation.ReconnectDeadlineUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(61)));
    }
}
