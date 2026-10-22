using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerRegistrationTestScenario
{
    public static async Task<(HostedRunnerRegistrationSeed Registration, Guid ResumeId)> SeedRetiredResumeAsync(
        ApplicationDbContext db, Guid userId)
    {
        var seed = Seed(db, userId);
        var runner = await db.HostedRunners.SingleAsync();
        runner.EnforcementState = HostedRunnerEnforcementStates.ResumePending;
        var resume = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = runner.Id, Action = HostedRunnerDeploymentActions.Resume,
            Status = HostedRunnerDeploymentStatuses.WaitingForReconnect, StartedAt = DateTime.UtcNow
        };
        db.HostedRunnerDeployments.Add(resume);
        await db.SaveChangesAsync();
        return (seed, resume.Id);
    }

    public static async Task AssertRetiredResumeIsUnchangedAsync(ApplicationDbContext db, Guid resumeId)
    {
        db.ChangeTracker.Clear();
        Assert.That((await db.HostedRunnerDeployments.SingleAsync(item => item.Id == resumeId)).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.WaitingForReconnect));
        var runner = await db.HostedRunners.SingleAsync();
        Assert.That(runner.EnforcementState, Is.EqualTo(HostedRunnerEnforcementStates.ResumePending));
        Assert.That(runner.RecoveredAtUtc, Is.Null);
    }

    public static HostedRunnerRegistrationSeed Seed(ApplicationDbContext db, Guid userId)
    {
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedUser(db, userId, planId);
        var runnerId = Guid.NewGuid();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RunnerId = runnerId,
            Name = "old-display-name",
            ContainerName = "automaton-hosted-runner-registration-test",
            Status = HostedRunnerStatuses.Reprovisioning,
            SpecificationSnapshot = new HostedRunnerSpecificationSnapshot
            {
                SourceSpecificationRevision = 1,
                Resources = new HostedRunnerResources { CpuLimit = 1, MemoryLimitMb = 1024, DiskLimitMb = 4096 }
            },
            PendingSpecificationSnapshot = new HostedRunnerSpecificationSnapshot
            {
                SourceSpecificationRevision = 2,
                Resources = new HostedRunnerResources
                {
                    CpuLimit = 2,
                    MemoryLimitMb = 2048,
                    MemoryReservationMb = 1024,
                    PidsLimit = 256,
                    DiskLimitMb = 8192
                }
            },
            CreatedAt = DateTime.UtcNow
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Reprovision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
            StartedAt = DateTime.UtcNow
        };
        hostedRunner.Deployments.Add(deployment);
        db.HostedRunners.Add(hostedRunner);
        db.SaveChanges();
        return new HostedRunnerRegistrationSeed(runnerId, hostedRunner.Id);
    }

    public static async Task AssertReprovisionCompletedAsync(ApplicationDbContext db, HostedRunnerRegistrationSeed seed)
    {
        var hostedRunner = await db.HostedRunners.SingleAsync(x => x.Id == seed.HostedRunnerId);
        var deployment = await db.HostedRunnerDeployments.SingleAsync(x => x.HostedRunnerId == seed.HostedRunnerId);
        Assert.That(hostedRunner.RunnerId, Is.EqualTo(seed.RunnerId));
        Assert.That(hostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Running));
        Assert.That(hostedRunner.PendingSpecificationSnapshot, Is.Null);
        Assert.That(hostedRunner.SpecificationSnapshot.SourceSpecificationRevision, Is.EqualTo(2));
        Assert.That(hostedRunner.SpecificationSnapshot.Resources.MemoryLimitMb, Is.EqualTo(2048));
        Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Succeeded));
        Assert.That(deployment.FinishedAt, Is.Not.Null);
        Assert.That(await db.AuditEvents.AnyAsync(x => x.EventType == AuditEventTypes.RunnerReprovisionSucceeded), Is.True);
    }
}

internal sealed record HostedRunnerRegistrationSeed(Guid RunnerId, Guid HostedRunnerId);
