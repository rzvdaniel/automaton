using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Constants;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerProvisioningAuditAssertions
{
    public static async Task AssertRequestedAuditAndQueuedRunnerAsync(
        ApplicationDbContext db,
        HostedRunnerProvisioningService service,
        Guid userId)
    {
        var runner = await service.EnsureHostedRunnerAsync(userId);
        Assert.That(runner, Is.Not.Null);
        Assert.That(runner!.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        await AssertProvisioningAuditWasWrittenAsync(db);
        await AssertProvisioningSecretsWereGrantedAsync(db);
    }

    private static async Task AssertProvisioningAuditWasWrittenAsync(ApplicationDbContext db)
    {
        var events = await db.AuditEvents.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.That(events.Select(x => x.EventType), Is.EqualTo(new[]
        {
            AuditEventTypes.HostedRunnerProvisioningRequested,
            AuditEventTypes.RunnerEnrollmentCreated
        }));
        Assert.That(events[0].Outcome, Is.EqualTo(AuditOutcomes.Success));
    }

    private static async Task AssertProvisioningSecretsWereGrantedAsync(ApplicationDbContext db)
    {
        Assert.That(await db.SecretVersions.CountAsync(), Is.EqualTo(1));
        Assert.That(await db.ExecutionSecretGrants.CountAsync(), Is.EqualTo(1));
    }
}
