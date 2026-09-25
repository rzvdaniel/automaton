using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

public class RunnerEnrollmentServiceTests
{
    [Test]
    public async Task Exchange_CreatesCapabilityBoundRunnerAndCredential_AndTokenCannotBeReused()
    {
        await using var db = CreateDbContext();
        var pending = await CreatePendingInfrastructureEnrollmentAsync(db);

        var response = await pending.Service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest
            {
                EnrollmentToken = pending.EnrollmentToken,
                RunnerName = "infra-nbg-01",
                RunnerVersion = "9.0.0",
                MachineName = "hetzner-01"
            },
            CancellationToken.None);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.ApiKey, Does.StartWith("ak_runner_"));
        Assert.That(response.Purpose, Is.EqualTo(Automaton.Core.Enums.RunnerPurposes.Infrastructure));
        Assert.That(response.Capabilities, Does.Contain(Automaton.Core.Enums.RunnerCapabilities.HostExecution));
        Assert.That(response.RunnerHostId, Is.EqualTo(pending.HostId));

        var runner = await db.Runners.SingleAsync();
        Assert.That(runner.InstallationId, Is.EqualTo(response.InstallationId));
        Assert.That(runner.CapabilitiesJson, Does.Contain(Automaton.Core.Enums.RunnerCapabilities.HostedRunnerProvisioning));
        Assert.That((await db.RunnerHosts.SingleAsync()).InfrastructureRunnerId, Is.EqualTo(runner.Id));

        var replay = await pending.Service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest { EnrollmentToken = pending.EnrollmentToken },
            CancellationToken.None);

        Assert.That(replay, Is.Null);
        Assert.That(await db.Runners.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task InfrastructureEnrollmentTokenValidation_IsReadOnlyAndRequiresAvailableHost()
    {
        await using var db = CreateDbContext();
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "Hetzner token bootstrap",
            Provider = RunnerHostProviders.Hetzner
        };
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var created = await service.CreateAsync(host.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.That(await service.IsInfrastructureEnrollmentTokenValidAsync(
            created.EnrollmentToken,
            CancellationToken.None), Is.True);
        Assert.That(await db.RunnerEnrollments.SingleAsync(), Has.Property(nameof(RunnerEnrollment.Status))
            .EqualTo(RunnerEnrollmentStatuses.Pending));

        host.InfrastructureRunnerId = Guid.NewGuid();
        await db.SaveChangesAsync();

        Assert.That(await service.IsInfrastructureEnrollmentTokenValidAsync(
            created.EnrollmentToken,
            CancellationToken.None), Is.False);
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

    [Test]
    public async Task HostedRunnerEnrollment_RejectsTimedOutAttemptAndAllowsFreshOperation()
    {
        using var db = CreateDbContext();
        var result = await HostedRunnerEnrollmentScenario.RunAsync(db, new RecordingAuditEventService());

        Assert.Multiple(() =>
        {
            Assert.That(result.PriorTokenRejected, Is.True);
            Assert.That(result.FreshGrantIssued, Is.True);
            Assert.That(result.ProtectedSecretStored, Is.True);
            Assert.That(result.EnrollmentTokenIsValid, Is.True);
            Assert.That(result.StoredHashExcludesToken, Is.True);
            Assert.That(result.EnrollmentStatus, Is.EqualTo(RunnerEnrollmentStatuses.Completed));
            Assert.That(result.RunnerCountForInstallation, Is.EqualTo(1));
            Assert.That(result.RunnerHasApiKey, Is.True);
            Assert.That(result.HostedRunnerUsesExchangedRunner, Is.True);
            Assert.That(result.SecretWasRevoked, Is.True);
            Assert.That(result.GrantWasConsumedAndRevoked, Is.True);
            Assert.That(result.ExchangeSucceeded, Is.True);
            Assert.That(result.ReplayRejected, Is.True);
            Assert.That(result.ApiKeyCount, Is.EqualTo(1));
        });
    }
    private static RunnerEnrollmentService CreateService(ApplicationDbContext db)
    {
        var httpContextAccessor = new HttpContextAccessor();
        var userContextService = new UserContextService(httpContextAccessor);
        var authorizationService = new ResourceAuthorizationService(db, userContextService);
        var auditService = new RecordingAuditEventService();
        return TestRunnerEnrollmentFactory.Create(db, auditService);
    }

    private static async Task<PendingInfrastructureEnrollment> CreatePendingInfrastructureEnrollmentAsync(
        ApplicationDbContext db)
    {
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "Hetzner Nuremberg",
            Provider = RunnerHostProviders.Hetzner
        };
        db.RunnerHosts.Add(host);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var enrollment = await service.CreateAsync(host.Id, Guid.NewGuid(), CancellationToken.None);
        return new PendingInfrastructureEnrollment(host.Id, service, enrollment.EnrollmentToken);
    }

    private sealed record PendingInfrastructureEnrollment(
        Guid HostId,
        RunnerEnrollmentService Service,
        string EnrollmentToken);

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
