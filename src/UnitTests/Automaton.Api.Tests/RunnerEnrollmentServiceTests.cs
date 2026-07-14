using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public class RunnerEnrollmentServiceTests
{
    [Test]
    public async Task Exchange_CreatesCapabilityBoundRunnerAndCredential_AndTokenCannotBeReused()
    {
        await using var db = CreateDbContext();
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "Hetzner Nuremberg",
            Provider = RunnerHostProviders.Hetzner
        };
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var requestedBy = Guid.NewGuid();
        var service = CreateService(db);
        var created = await service.CreateAsync(host.Id, requestedBy, CancellationToken.None);

        var response = await service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest
            {
                EnrollmentToken = created.EnrollmentToken,
                RunnerName = "infra-nbg-01",
                RunnerVersion = "9.0.0",
                MachineName = "hetzner-01"
            },
            CancellationToken.None);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.ApiKey, Does.StartWith("ak_runner_"));
        Assert.That(response.Purpose, Is.EqualTo(Automaton.Core.Enums.RunnerPurposes.Infrastructure));
        Assert.That(response.Capabilities, Does.Contain(Automaton.Core.Enums.RunnerCapabilities.HostExecution));
        Assert.That(response.RunnerHostId, Is.EqualTo(host.Id));

        var runner = await db.Runners.SingleAsync();
        Assert.That(runner.InstallationId, Is.EqualTo(response.InstallationId));
        Assert.That(runner.CapabilitiesJson, Does.Contain(Automaton.Core.Enums.RunnerCapabilities.HostedRunnerProvisioning));
        Assert.That((await db.RunnerHosts.SingleAsync()).InfrastructureRunnerId, Is.EqualTo(runner.Id));

        var replay = await service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest { EnrollmentToken = created.EnrollmentToken },
            CancellationToken.None);

        Assert.That(replay, Is.Null);
        Assert.That(await db.Runners.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Create_RevokesPreviousPendingEnrollment()
    {
        await using var db = CreateDbContext();
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "Hetzner Falkenstein",
            Provider = RunnerHostProviders.Hetzner
        };
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var first = await service.CreateAsync(host.Id, Guid.NewGuid(), CancellationToken.None);
        var second = await service.CreateAsync(host.Id, Guid.NewGuid(), CancellationToken.None);

        var enrollments = await db.RunnerEnrollments.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.That(enrollments, Has.Count.EqualTo(2));
        Assert.That(enrollments[0].Status, Is.EqualTo(RunnerEnrollmentStatuses.Revoked));
        Assert.That(enrollments[1].Status, Is.EqualTo(RunnerEnrollmentStatuses.Pending));
        Assert.That(first.EnrollmentToken, Is.Not.EqualTo(second.EnrollmentToken));
    }

    private static RunnerEnrollmentService CreateService(ApplicationDbContext db)
    {
        var httpContextAccessor = new HttpContextAccessor();
        var userContextService = new UserContextService(httpContextAccessor);
        var authorizationService = new ResourceAuthorizationService(db, userContextService);
        var auditService = new RecordingAuditEventService();
        var apiKeyService = new ApiKeyService(
            db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiKeyService>.Instance,
            authorizationService,
            auditService);

        return new RunnerEnrollmentService(
            db,
            apiKeyService,
            auditService,
            Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunnerEnrollmentService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"runner-enrollment-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class RecordingAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
