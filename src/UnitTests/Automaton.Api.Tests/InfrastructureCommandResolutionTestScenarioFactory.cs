using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class InfrastructureCommandResolutionTestScenarioFactory
{
    public static async Task<ResolutionFixture> CreateAsync(
        ApplicationDbContext db,
        string status = HostedRunnerDeploymentStatuses.Dispatching,
        bool expired = false,
        bool delivered = false,
        bool consumed = false,
        ITransactionalEventExecutor? transactionalEventExecutor = null)
    {
        var operation = await SeedOperationAsync(db, status);
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var grant = await CreateGrantAsync(db, operation, protector);
        ApplyGrantEvidence(grant, expired, delivered, consumed);
        await db.SaveChangesAsync();
        var service = CreateService(db, protector, transactionalEventExecutor);
        return new ResolutionFixture(
            operation.ApiKeyId,
            operation.Runner,
            operation.Host,
            operation.HostedRunner,
            operation.Deployment,
            grant.Id,
            service);
    }

    private static async Task<ResolutionOperation> SeedOperationAsync(ApplicationDbContext db, string status)
    {
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            ApiKeyId = apiKeyId,
            Name = "infra",
            Purpose = RunnerPurposes.Infrastructure
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host",
            Provider = "test",
            InfrastructureRunnerId = runner.Id,
            InfrastructureRunner = runner
        };
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "hosted",
            ContainerName = "hosted",
            RunnerHostId = host.Id,
            Status = HostedRunnerStatuses.Provisioning
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunner.Id,
            HostedRunner = hostedRunner,
            Action = HostedRunnerDeploymentActions.Reprovision,
            Status = status
        };
        db.AddRange(runner, host, hostedRunner, deployment);
        await db.SaveChangesAsync();
        return new ResolutionOperation(apiKeyId, runner, host, hostedRunner, deployment);
    }

    private static async Task<ExecutionSecretGrant> CreateGrantAsync(
        ApplicationDbContext db,
        ResolutionOperation operation,
        DataProtectionSecretProtector protector)
    {
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var scope = new SecretGrantScope(
            operation.Deployment.Id,
            InfrastructureAppTrust.PlatformAppId,
            "reprovision-hosted-runner",
            operation.Runner.Id,
            operation.Host.Id,
            SecretOwnerTypes.HostedRunner,
            operation.HostedRunner.Id);
        var version = await store.CreateVersionAsync(
            new SecretOwner(SecretOwnerTypes.HostedRunner, operation.HostedRunner.Id),
            "runner-secret",
            "secret-value",
            DateTime.UtcNow.AddMinutes(10),
            CancellationToken.None);
        var grantId = await grants.CreateGrantAsync(
            version.SecretVersionId,
            scope,
            "AUTOMATON_TOKEN",
            DateTime.UtcNow.AddMinutes(5),
            CancellationToken.None);
        return await db.ExecutionSecretGrants.SingleAsync(x => x.Id == grantId);
    }

    private static void ApplyGrantEvidence(ExecutionSecretGrant grant, bool expired, bool delivered, bool consumed)
    {
        if (expired)
            grant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        if (delivered)
        {
            grant.DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-2);
            grant.ReadCount = 1;
        }
        if (consumed)
        {
            grant.LeasedAtUtc = DateTime.UtcNow.AddMinutes(-2);
            grant.ConsumedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }
    }

    private static InfrastructureCommandCallbackService CreateService(
        ApplicationDbContext db,
        DataProtectionSecretProtector protector,
        ITransactionalEventExecutor? transactionalEventExecutor)
    {
        var grants = new ExecutionSecretGrantService(db, protector);
        var lifecycle = new InfrastructureActionProvisioningHandler(
            db,
            new NoOpAuditEventService(),
            Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()));
        var results = new InfrastructureCommandResultService(db, grants, lifecycle);
        return new InfrastructureCommandCallbackService(
            db,
            grants,
            lifecycle,
            results,
            transactionalEventExecutor);
    }

    private sealed record ResolutionOperation(
        Guid ApiKeyId,
        Runner Runner,
        RunnerHost Host,
        HostedRunner HostedRunner,
        HostedRunnerDeployment Deployment);

    private sealed class NoOpAuditEventService : IAuditEventService
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

internal sealed record ResolutionFixture(
    Guid ApiKeyId,
    Runner Runner,
    RunnerHost Host,
    HostedRunner HostedRunner,
    HostedRunnerDeployment Deployment,
    Guid GrantId,
    InfrastructureCommandCallbackService Service);
