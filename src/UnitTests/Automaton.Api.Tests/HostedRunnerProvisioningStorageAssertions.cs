using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerProvisioningStorageAssertions
{
    public static void AssertResolvedSpecification(HostedRunnerPreparationResult prepared, Guid specificationId)
    {
        var snapshot = prepared.HostedRunner.SpecificationSnapshot;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SourceSpecificationId, Is.EqualTo(specificationId));
            Assert.That(snapshot.Resources.CpuLimit, Is.EqualTo(0.75m));
            Assert.That(snapshot.Resources.MemoryLimitMb, Is.EqualTo(1024));
            Assert.That(snapshot.Resources.DiskLimitMb, Is.EqualTo(3072));
            Assert.That(snapshot.Resources.PidsLimit, Is.EqualTo(192));
            Assert.That(snapshot.Resources.MaximumApps, Is.EqualTo(7));
            Assert.That(snapshot.Resources.MemoryReservationMb, Is.EqualTo(512));
            Assert.That(snapshot.Resources.SharedMemoryMb, Is.EqualTo(96));
            Assert.That(snapshot.Resources.DockerLogMaxSizeMb, Is.EqualTo(20));
            Assert.That(snapshot.Resources.DockerLogMaxFiles, Is.EqualTo(4));
            Assert.That(prepared.HostedRunner.StorageProjectId, Is.GreaterThan(0));
        });
    }

    public static async Task AssertAccountProjectRecordedAsync(
        ApplicationDbContext db,
        HostedRunnerPreparationResult prepared,
        Guid userId)
    {
        var allocation = await db.AccountStorageAllocations.SingleAsync(x => x.AccountId == userId);
        Assert.That(allocation.StorageProjectId, Is.EqualTo(prepared.HostedRunner.StorageProjectId));
    }

    public static async Task AssertReprovisionReusesAccountProjectAsync(
        ApplicationDbContext db,
        HostedRunnerProvisioningService service,
        Guid userId)
    {
        var created = await service.PrepareProvisioningAsync(userId, Guid.Empty, forceProvision: true);
        var originalProjectId = created.HostedRunner.StorageProjectId;
        var specification = await db.HostedRunnerSpecifications
            .SingleAsync(x => x.Id == created.HostedRunner.HostedRunnerSpecificationId);
        specification.Resources.DiskLimitMb = 1024;
        await db.SaveChangesAsync();

        var reprovisioned = await service.PrepareProvisioningAsync(userId, Guid.Empty, forceProvision: true);
        Assert.That(reprovisioned.HostedRunner.PendingSpecificationSnapshot!.Resources.DiskLimitMb, Is.EqualTo(1024));
        Assert.That(reprovisioned.HostedRunner.StorageProjectId, Is.EqualTo(originalProjectId));

        reprovisioned.HostedRunner.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var recreated = await service.PrepareProvisioningAsync(userId, Guid.Empty, forceProvision: true);

        Assert.Multiple(() =>
        {
            Assert.That(recreated.HostedRunner.Id, Is.Not.EqualTo(reprovisioned.HostedRunner.Id));
            Assert.That(recreated.HostedRunner.StorageProjectId, Is.EqualTo(originalProjectId));
        });
        Assert.That(await db.HostedRunners.CountAsync(x => x.UserId == userId), Is.EqualTo(2));
        Assert.That(await db.HostedRunners.Where(x => x.UserId == userId)
            .Select(x => x.StorageProjectId).Distinct().CountAsync(), Is.EqualTo(1));
        Assert.That(await db.AccountStorageAllocations.CountAsync(x => x.AccountId == userId), Is.EqualTo(1));
    }
}
