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
public class HostedRunnerReconnectTimeoutTests
{


    [Test]
    public async Task RestartReconnectTimeoutFailsOnceWithDistinctReason()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var now = DateTime.UtcNow;
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "timed-out-restart",
            ContainerName = "timed-out-restart", Status = HostedRunnerStatuses.Running
        };
        var operation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Restart,
            Status = HostedRunnerDeploymentStatuses.WaitingForReconnect,
            ReconnectAfterUtc = now.AddMinutes(-3), ReconnectDeadlineUtc = now.AddMinutes(-1)
        };
        hostedRunner.Deployments.Add(operation);
        db.HostedRunners.Add(hostedRunner);
        db.SaveChanges();
        var reconciler = new HostedRunnerRestartReconciler(
            db, new HostedRunnerAuditNoOpAuditEventService(), NullLogger<HostedRunnerRestartReconciler>.Instance);

        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.EqualTo(1));
        Assert.That(await reconciler.ReconcileAsync(now, CancellationToken.None), Is.Zero);
        Assert.That(operation.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
        Assert.That(operation.Error, Is.EqualTo(HostedRunnerDeploymentErrors.RestartReconnectTimedOut));
        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
    }
}
