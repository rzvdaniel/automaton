using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerStorageVerificationControllerTests
{
    [Test]
    public async Task VerifyStorage_RejectsRunnerBeforeProvisioningCompletes()
    {
        await using var db = CreateDb();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted",
            Status = HostedRunnerStatuses.Provisioning
        };
        db.HostedRunners.Add(hostedRunner);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.VerifyStorage(hostedRunner.Id, CancellationToken.None);

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
        Assert.That(await db.HostedRunnerStorageVerifications.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task VerifyStorage_AcceptsRunningProvisionedRunner()
    {
        await using var db = CreateDb();
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted",
            ContainerId = "container-id", Status = HostedRunnerStatuses.Running
        };
        db.HostedRunners.Add(hostedRunner);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.VerifyStorage(hostedRunner.Id, CancellationToken.None);

        Assert.That(result, Is.TypeOf<AcceptedResult>());
        var verification = await db.HostedRunnerStorageVerifications.SingleAsync();
        Assert.That(verification.Status, Is.EqualTo(StorageVerificationStatuses.Pending));
        Assert.That(verification.Reason, Is.EqualTo("AdministratorRequested"));
    }

    private static AdminHostedRunnerStorageController CreateController(ApplicationDbContext db) =>
        new(db, new NoOpAuditEventService(), null!, null!);

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
