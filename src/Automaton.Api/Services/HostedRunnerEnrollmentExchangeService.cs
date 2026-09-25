using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Completes enrollment of Hosted Runners provisioned by an Infrastructure Runner.
/// </summary>
public sealed class HostedRunnerEnrollmentExchangeService(
    ApplicationDbContext db,
    ApiKeyService apiKeyService,
    IAuditEventService auditEventService,
    ILogger<HostedRunnerEnrollmentExchangeService> logger)
{
    public async Task<RunnerEnrollmentExchangeResponse?> ExchangeAsync(
        RunnerEnrollment enrollment,
        RunnerHost host,
        RunnerEnrollmentExchangeRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var hostedRunner = await LoadWaitingHostedRunnerAsync(enrollment, host, now, cancellationToken);
        if (hostedRunner is null)
            return null;

        var runnerName = string.IsNullOrWhiteSpace(request.RunnerName)
            ? hostedRunner.Name
            : request.RunnerName.Trim();
        if (runnerName.Length > 120)
            return null;

        var capabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed);
        var registration = await RegisterRunnerAsync(
            enrollment, hostedRunner, request, runnerName, capabilities, cancellationToken);
        if (registration is null)
            return null;

        var runner = registration.Runner;
        var apiKey = registration.ApiKey;
        runner.ApiKeyId = apiKey.Id;
        hostedRunner.RunnerId = runner.Id;
        hostedRunner.ApiKeyId = apiKey.Id;
        hostedRunner.LastError = null;
        hostedRunner.UpdatedAt = now;
        enrollment.Status = RunnerEnrollmentStatuses.Completed;
        enrollment.UsedAtUtc = now;
        enrollment.RunnerId = runner.Id;

        await RevokeEnrollmentSecretGrantsAsync(enrollment.SecretVersionId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Hosted Runner '{hostedRunner.Name}' exchanged its one-time enrollment credential.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = hostedRunner.UserId,
            TargetUserId = hostedRunner.UserId,
            Metadata = new { HostedRunnerId = hostedRunner.Id, RunnerId = runner.Id, RunnerHostId = host.Id, apiKey.KeyPrefix }
        }, cancellationToken);

        logger.LogInformation("Hosted Runner {HostedRunnerId} enrolled as Runner {RunnerId}", hostedRunner.Id, runner.Id);
        return new RunnerEnrollmentExchangeResponse
        {
            RunnerId = runner.Id,
            RunnerHostId = host.Id,
            RunnerName = runner.Name,
            ApiKey = registration.RawApiKey,
            Purpose = runner.Purpose,
            Capabilities = capabilities,
            InstallationId = enrollment.InstallationId
        };
    }

    private async Task<HostedRunner?> LoadWaitingHostedRunnerAsync(
        RunnerEnrollment enrollment,
        RunnerHost host,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var hostedRunner = await db.HostedRunners
            .FirstOrDefaultAsync(x => x.Id == enrollment.HostedRunnerId && x.DeletedAt == null, cancellationToken);
        var operation = enrollment.OperationId.HasValue
            ? await db.HostedRunnerDeployments.FirstOrDefaultAsync(
                x => x.Id == enrollment.OperationId.Value && x.HostedRunnerId == enrollment.HostedRunnerId,
                cancellationToken)
            : null;
        if (IsWaitingEnrollmentScopeValid(enrollment, host, hostedRunner, operation, now))
            return hostedRunner;

        enrollment.Status = RunnerEnrollmentStatuses.Failed;
        enrollment.FailureReason = "The hosted Runner enrollment scope is no longer valid.";
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static bool IsWaitingEnrollmentScopeValid(
        RunnerEnrollment enrollment,
        RunnerHost host,
        HostedRunner? hostedRunner,
        HostedRunnerDeployment? operation,
        DateTime now) =>
        hostedRunner is not null
        && operation is not null
        && operation.Status == HostedRunnerDeploymentStatuses.WaitingForRegistration
        && operation.RegistrationDeadlineUtc is DateTime registrationDeadline
        && registrationDeadline > now
        && hostedRunner.RunnerHostId == host.Id
        && hostedRunner.UserId == enrollment.RequestedByUserId
        && host.InfrastructureRunnerId is not null
        && hostedRunner.Status is HostedRunnerStatuses.Provisioning or HostedRunnerStatuses.Reprovisioning;

    private async Task<HostedRunnerRegistration?> RegisterRunnerAsync(
        RunnerEnrollment enrollment,
        HostedRunner hostedRunner,
        RunnerEnrollmentExchangeRequest request,
        string runnerName,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken)
    {
        var (rawApiKey, apiKey) = await apiKeyService.CreateApiKeyAsync(
            hostedRunner.UserId,
            $"Hosted Runner - {runnerName}"[..Math.Min(100, $"Hosted Runner - {runnerName}".Length)],
            ApiKeyScope.Runner,
            "Issued after one-time hosted Runner enrollment.",
            saveChanges: false,
            source: ApiKeySource.RunnerProvisioning);

        var runner = await db.Runners.Include(x => x.RunnerUsers)
            .FirstOrDefaultAsync(x => x.InstallationId == enrollment.InstallationId, cancellationToken);
        if (runner is not null && !IsRunnerIdentityOwnedByUser(runner, hostedRunner.UserId))
        {
            enrollment.Status = RunnerEnrollmentStatuses.Failed;
            enrollment.FailureReason = "The hosted Runner installation identity belongs to another Runner.";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        runner = runner is null
            ? CreateRunner(enrollment, hostedRunner.UserId, request, runnerName, capabilities)
            : await UpdateExistingRunnerAsync(runner, request, runnerName, capabilities, cancellationToken);
        return new HostedRunnerRegistration(runner, apiKey, rawApiKey);
    }

    private static bool IsRunnerIdentityOwnedByUser(Runner runner, Guid userId) =>
        string.Equals(runner.Purpose, RunnerPurposes.Managed, StringComparison.OrdinalIgnoreCase)
        && runner.RunnerUsers.Any(x => x.UserId == userId);

    private Runner CreateRunner(
        RunnerEnrollment enrollment,
        Guid userId,
        RunnerEnrollmentExchangeRequest request,
        string runnerName,
        IReadOnlyList<string> capabilities)
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = runnerName,
            Status = RunnerConnectivityStatuses.Unknown,
            Version = request.RunnerVersion,
            MachineName = request.MachineName,
            Purpose = RunnerPurposes.Managed,
            CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, RunnerPurposes.Managed),
            InstallationId = enrollment.InstallationId
        };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = userId });
        return runner;
    }

    private async Task<Runner> UpdateExistingRunnerAsync(
        Runner runner,
        RunnerEnrollmentExchangeRequest request,
        string runnerName,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken)
    {
        if (runner.ApiKeyId.HasValue)
        {
            var oldKey = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == runner.ApiKeyId.Value, cancellationToken);
            if (oldKey is not null)
                oldKey.IsActive = false;
        }

        runner.Name = runnerName;
        runner.Status = RunnerConnectivityStatuses.Unknown;
        runner.Version = request.RunnerVersion;
        runner.MachineName = request.MachineName;
        runner.ConnectionId = null;
        runner.Purpose = RunnerPurposes.Managed;
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, RunnerPurposes.Managed);
        return runner;
    }

    private async Task RevokeEnrollmentSecretGrantsAsync(
        Guid? secretVersionId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!secretVersionId.HasValue)
            return;

        var version = await db.SecretVersions.FirstOrDefaultAsync(x => x.Id == secretVersionId, cancellationToken);
        if (version is not null)
            version.RevokedAtUtc ??= now;

        var grants = await db.ExecutionSecretGrants
            .Where(x => x.SecretVersionId == secretVersionId && x.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var grant in grants)
        {
            if (grant.LeasedAtUtc.HasValue)
                grant.ConsumedAtUtc ??= now;
            grant.RevokedAtUtc = now;
        }
    }

    private sealed record HostedRunnerRegistration(Runner Runner, ApiKey ApiKey, string RawApiKey);
}
