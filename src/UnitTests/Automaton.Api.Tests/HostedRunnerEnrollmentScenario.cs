using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class HostedRunnerEnrollmentScenario
{
    public static async Task<HostedRunnerEnrollmentScenarioResult> RunAsync(
        ApplicationDbContext db,
        IAuditEventService auditEventService)
    {
        var state = await SeedAsync(db);
        var services = TestRunnerEnrollmentFactory.CreateWithSecretGrantService(db, auditEventService);
        var expired = await CreateMaterialAndLeaseAsync(state, services, state.PriorOperation.Id);
        await MarkPriorOperationTimedOutAsync(db, state.PriorOperation);
        var current = await CreateMaterialAndLeaseAsync(state, services, state.CurrentOperation.Id);
        var priorTokenRejected = await services.Service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest { EnrollmentToken = expired.EnrollmentToken },
            CancellationToken.None) is null;
        var completion = await CompleteExchangeAsync(db, state, services, current);

        return completion with
        {
            PriorTokenRejected = priorTokenRejected,
            FreshGrantIssued = current.Material.SecretGrantId != expired.Material.SecretGrantId
        };
    }

    private static async Task<HostedRunnerEnrollmentScenarioState> SeedAsync(ApplicationDbContext db)
    {
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
        var managedRunner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = hostedRunner.Name,
            Purpose = RunnerPurposes.Managed,
            InstallationId = $"hr_{hostedRunner.Id:N}"
        };
        db.Runners.AddRange(infrastructureRunner, managedRunner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = managedRunner.Id, UserId = userId });
        db.RunnerHosts.Add(host);
        db.HostedRunners.Add(hostedRunner);
        var priorOperation = CreateOperation(hostedRunner, HostedRunnerDeploymentActions.Provision);
        var currentOperation = CreateOperation(hostedRunner, HostedRunnerDeploymentActions.Reprovision);
        db.HostedRunnerDeployments.AddRange(priorOperation, currentOperation);
        await db.SaveChangesAsync();
        return new HostedRunnerEnrollmentScenarioState(
            infrastructureRunner,
            host,
            hostedRunner,
            managedRunner,
            priorOperation,
            currentOperation);
    }

    private static HostedRunnerDeployment CreateOperation(HostedRunner hostedRunner, string action)
        => new()
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunner.Id,
            Action = action,
            Status = HostedRunnerDeploymentStatuses.WaitingForRegistration,
            RegistrationDeadlineUtc = DateTime.UtcNow.AddMinutes(15)
        };

    private static async Task<EnrollmentMaterialLease> CreateMaterialAndLeaseAsync(
        HostedRunnerEnrollmentScenarioState state,
        RunnerEnrollmentTestServices services,
        Guid operationId)
    {
        var material = await services.Service.CreateHostedRunnerAsync(
            state.HostedRunner.Id,
            operationId,
            "provision-hosted-runner",
            state.InfrastructureRunner.Id,
            CancellationToken.None);
        var lease = await services.SecretGrants.LeaseAsync(
            CreateScope(state, operationId),
            [material.SecretGrantId],
            CancellationToken.None);
        return new EnrollmentMaterialLease(
            material,
            lease.EnvironmentVariables["AUTOMATON_PROVISION_RUNNER_ENROLLMENT_TOKEN"]);
    }

    private static SecretGrantScope CreateScope(
        HostedRunnerEnrollmentScenarioState state,
        Guid operationId)
        => new(
            operationId,
            InfrastructureAppTrust.PlatformAppId,
            "provision-hosted-runner",
            state.InfrastructureRunner.Id,
            state.Host.Id,
            SecretOwnerTypes.HostedRunner,
            state.HostedRunner.Id);

    private static async Task MarkPriorOperationTimedOutAsync(
        ApplicationDbContext db,
        HostedRunnerDeployment priorOperation)
    {
        priorOperation.Status = HostedRunnerDeploymentStatuses.Failed;
        priorOperation.RegistrationDeadlineUtc = DateTime.UtcNow.AddMinutes(-1);
        priorOperation.Error = HostedRunnerDeploymentErrors.RegistrationTimedOut;
        priorOperation.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task<HostedRunnerEnrollmentScenarioResult> CompleteExchangeAsync(
        ApplicationDbContext db,
        HostedRunnerEnrollmentScenarioState state,
        RunnerEnrollmentTestServices services,
        EnrollmentMaterialLease current)
    {
        var enrollment = await db.RunnerEnrollments.SingleAsync(item => item.OperationId == state.CurrentOperation.Id);
        var secretVersion = await db.SecretVersions.SingleAsync(item => item.Id == enrollment.SecretVersionId);
        var tokenIsValid = current.EnrollmentToken.StartsWith("re_", StringComparison.Ordinal);
        var exchange = await services.Service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest
            {
                EnrollmentToken = current.EnrollmentToken,
                RunnerName = state.HostedRunner.Name,
                RunnerVersion = "test",
                MachineName = "managed-host"
            },
            CancellationToken.None);
        var persistedGrant = await db.ExecutionSecretGrants.SingleAsync(item => item.Id == current.Material.SecretGrantId);
        var replay = await services.Service.ExchangeAsync(
            new RunnerEnrollmentExchangeRequest { EnrollmentToken = current.EnrollmentToken },
            CancellationToken.None);
        var runnerCount = await db.Runners.CountAsync(item => item.InstallationId == state.ManagedRunner.InstallationId);
        var managedRunner = await db.Runners.SingleAsync(item => item.Id == state.ManagedRunner.Id);
        var hostedRunner = await db.HostedRunners.SingleAsync();

        return new HostedRunnerEnrollmentScenarioResult(
            PriorTokenRejected: false,
            FreshGrantIssued: false,
            ProtectedSecretStored: !secretVersion.ProtectedValue.StartsWith("re_", StringComparison.Ordinal),
            EnrollmentTokenIsValid: tokenIsValid,
            StoredHashExcludesToken: !enrollment.EnrollmentTokenHash.Contains(current.EnrollmentToken, StringComparison.Ordinal),
            EnrollmentStatus: enrollment.Status,
            RunnerCountForInstallation: runnerCount,
            RunnerHasApiKey: managedRunner.ApiKeyId is not null,
            HostedRunnerUsesExchangedRunner: exchange is not null && hostedRunner.RunnerId == exchange.RunnerId,
            SecretWasRevoked: secretVersion.RevokedAtUtc is not null,
            GrantWasConsumedAndRevoked: persistedGrant.ConsumedAtUtc is not null && persistedGrant.RevokedAtUtc is not null,
            ExchangeSucceeded: exchange is not null &&
                exchange.ApiKey.StartsWith("ak_runner_", StringComparison.Ordinal) &&
                exchange.Purpose == RunnerPurposes.Managed &&
                exchange.RunnerId == state.ManagedRunner.Id,
            ReplayRejected: replay is null,
            ApiKeyCount: await db.ApiKeys.CountAsync());
    }

    private sealed record HostedRunnerEnrollmentScenarioState(
        Runner InfrastructureRunner,
        RunnerHost Host,
        HostedRunner HostedRunner,
        Runner ManagedRunner,
        HostedRunnerDeployment PriorOperation,
        HostedRunnerDeployment CurrentOperation);

    private sealed record EnrollmentMaterialLease(
        HostedRunnerEnrollmentMaterial Material,
        string EnrollmentToken);
}

internal sealed record HostedRunnerEnrollmentScenarioResult(
    bool PriorTokenRejected,
    bool FreshGrantIssued,
    bool ProtectedSecretStored,
    bool EnrollmentTokenIsValid,
    bool StoredHashExcludesToken,
    string EnrollmentStatus,
    int RunnerCountForInstallation,
    bool RunnerHasApiKey,
    bool HostedRunnerUsesExchangedRunner,
    bool SecretWasRevoked,
    bool GrantWasConsumedAndRevoked,
    bool ExchangeSucceeded,
    bool ReplayRejected,
    int ApiKeyCount);
