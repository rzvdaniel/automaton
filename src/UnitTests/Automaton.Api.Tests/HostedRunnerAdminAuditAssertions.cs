using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerAdminAuditAssertions
{
    public static async Task AssertRestartRequestQueuedAsync(
        ApplicationDbContext db,
        HostedRunnerManagementService managementService,
        Guid hostedRunnerId)
    {
        await managementService.RestartAsAdminAsync(hostedRunnerId, CancellationToken.None);
        var auditEvents = await db.AuditEvents.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.That(auditEvents.Select(x => x.EventType), Is.EqualTo(new[] { AuditEventTypes.RunnerRestartRequested }));
        Assert.That(db.HostedRunnerDeployments.Single().Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
    }
}
