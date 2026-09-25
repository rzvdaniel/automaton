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
public class HostedRunnerRestartHeartbeatTests
{

    [Test]
    public async Task RestartCompletesOnlyAfterHeartbeatOnNewConnectionSession()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var seed = HostedRunnerRestartLifecycleScenario.Seed(db, userId);
        var result = await HostedRunnerRestartLifecycleScenario.CompleteAsync(db, seed);

        Assert.That(result.RemainedWaitingAfterOldSession, Is.True);
        Assert.That(result.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Succeeded));
        Assert.That(result.FinishedAt, Is.Not.Null);
        Assert.That(result.SuccessfulAuditCount, Is.EqualTo(1));
    }
}
