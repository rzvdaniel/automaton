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
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var infrastructureRunner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "infrastructure-host-1",
            Purpose = RunnerPurposes.Infrastructure,
            CapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                RunnerPurposes.Infrastructure)
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-1",
            Provider = RunnerHostProviders.Hetzner,
            InfrastructureRunnerId = infrastructureRunner.Id
        };
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RunnerHostId = host.Id,
            Name = "managed-user",
            ContainerName = "automaton-managed-user",
            Status = HostedRunnerStatuses.Provisioning
        };
        var priorManagedRunner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = hostedRunner.Name,
            Purpose = RunnerPurposes.Managed,
            InstallationId = $"hr_{hostedRunner.Id:N}"
        };
        var priorManagedRunnerUser = new RunnerUser
        {
            RunnerId = priorManagedRunner.Id,
            UserId = userId
        };
        var operationId = Guid.NewGuid();
        var priorOperation = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Provision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
            RegistrationDeadlineUtc = DateTime.UtcNow.AddMinutes(15)
        };
        var currentOperation = new HostedRunnerDeployment
        {
            Id = operationId,
            HostedRunnerId = hostedRunner.Id,
            Action = HostedRunnerDeploymentActions.Reprovision,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
            RegistrationDeadlineUtc = DateTime.UtcNow.AddMinutes(15)
        };
        db.Runners.AddRange(infrastructureRunner, priorManagedRunner);
        db.RunnerUsers.Add(priorManagedRunnerUser);
        db.RunnerHosts.Add(host);
        db.HostedRunners.Add(hostedRunner);
        db.HostedRunnerDeployments.AddRange(priorOperation, currentOperation);
        await db.SaveChangesAsync();

        var auditService = new RecordingAuditEventService();
        var apiKeyService = new ApiKeyService(
            db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiKeyService>.Instance,
            authorizationService: null!,
            auditService);
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var grantService = new ExecutionSecretGrantService(db, protector);
        var service = new RunnerEnrollmentService(
            db,
            apiKeyService,
            auditService,
            Options.Create(new RunnerEnrollmentOptions { TokenLifetimeMinutes = 15 }),
            Options.Create(new HostedRunnerOptions { DockerImage = "test/automaton-runner:latest" }),
            new SecretStore(db, protector),
            grantService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunnerEnrollmentService>.Instance);

        var priorMaterial = await service.CreateHostedRunnerAsync(
            hostedRunner.Id,
            priorOperation.Id,
            "provision-hosted-runner",
            infrastructureRunner.Id,
            CancellationToken.None);
        var priorLease = await grantService.LeaseAsync(
            new SecretGrantScope(
                priorOperation.Id,
                InfrastructureAppTrust.PlatformAppId,
                "provision-hosted-runner",
                infrastructureRunner.Id,
                host.Id,
                SecretOwnerTypes.HostedRunner,
                hostedRunner.Id),
            [priorMaterial.SecretGrantId],
            CancellationToken.None);
        var priorToken = priorLease.EnvironmentVariables["AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN"];
        priorOperation.Status = HostedRunnerDeploymentStatuses.Failed;
        priorOperation.RegistrationDeadlineUtc = DateTime.UtcNow.AddMinutes(-1);
        priorOperation.Error = HostedRunnerDeploymentErrors.RegistrationTimedOut;
        priorOperation.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var material = await service.CreateHostedRunnerAsync(
            hostedRunner.Id,
            operationId,
            "provision-hosted-runner",
            infrastructureRunner.Id,
            CancellationToken.None);
        Assert.That(material.SecretGrantId, Is.Not.EqualTo(priorMaterial.SecretGrantId));
        Assert.That(await service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest { EnrollmentToken = priorToken },
            CancellationToken.None), Is.Null);

        var currentEnrollment = await db.RunnerEnrollments.SingleAsync(x => x.OperationId == operationId);
        var storedVersion = await db.SecretVersions.SingleAsync(x => x.Id == currentEnrollment.SecretVersionId);
        Assert.That(storedVersion.ProtectedValue, Does.Not.StartWith("re_"));
        var lease = await grantService.LeaseAsync(
            new SecretGrantScope(
                operationId,
                InfrastructureAppTrust.PlatformAppId,
                "provision-hosted-runner",
                infrastructureRunner.Id,
                host.Id,
                SecretOwnerTypes.HostedRunner,
                hostedRunner.Id),
            [material.SecretGrantId],
            CancellationToken.None);
        var enrollmentToken = lease.EnvironmentVariables["AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN"];
        Assert.That(enrollmentToken, Does.StartWith("re_"));
        Assert.That(currentEnrollment.EnrollmentTokenHash, Does.Not.Contain(enrollmentToken));
        Assert.That(currentEnrollment.OperationId, Is.EqualTo(operationId));

        var exchange = await service.ExchangeAsync(new RunnerEnrollmentExchangeRequest
        {
            EnrollmentToken = enrollmentToken,
            RunnerName = hostedRunner.Name,
            RunnerVersion = "test",
            MachineName = "managed-host"
        }, CancellationToken.None);

        Assert.That(exchange, Is.Not.Null);
        Assert.That(exchange!.ApiKey, Does.StartWith("ak_runner_"));
        Assert.That(exchange.Purpose, Is.EqualTo(RunnerPurposes.Managed));
        Assert.That(exchange.RunnerId, Is.EqualTo(priorManagedRunner.Id));
        Assert.That(await db.Runners.CountAsync(x => x.InstallationId == priorManagedRunner.InstallationId), Is.EqualTo(1));
        Assert.That((await db.Runners.SingleAsync(x => x.Id == priorManagedRunner.Id)).ApiKeyId, Is.Not.Null);
        Assert.That((await db.HostedRunners.SingleAsync()).RunnerId, Is.EqualTo(exchange.RunnerId));
        Assert.That((await db.RunnerEnrollments.SingleAsync(x => x.OperationId == operationId)).Status, Is.EqualTo(RunnerEnrollmentStatuses.Completed));
        Assert.That((await db.SecretVersions.SingleAsync(x => x.Id == currentEnrollment.SecretVersionId)).RevokedAtUtc, Is.Not.Null);
        var persistedGrant = await db.ExecutionSecretGrants.SingleAsync(x => x.Id == material.SecretGrantId);
        Assert.That(persistedGrant.ConsumedAtUtc, Is.Not.Null);
        Assert.That(persistedGrant.RevokedAtUtc, Is.Not.Null);

        var replay = await service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest { EnrollmentToken = enrollmentToken },
            CancellationToken.None);
        Assert.That(replay, Is.Null);
        Assert.That(await db.ApiKeys.CountAsync(), Is.EqualTo(1));
    }

    private static RunnerEnrollmentService CreateService(ApplicationDbContext db)
    {
        var httpContextAccessor = new HttpContextAccessor();
        var userContextService = new UserContextService(httpContextAccessor);
        var authorizationService = new ResourceAuthorizationService(db, userContextService);
        var auditService = new RecordingAuditEventService();
        return TestRunnerEnrollmentFactory.Create(db, auditService);
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
