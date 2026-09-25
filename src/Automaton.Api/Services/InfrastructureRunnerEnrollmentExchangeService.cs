using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Creates the Infrastructure Runner identity after enrollment has been validated.
/// </summary>
public sealed class InfrastructureRunnerEnrollmentExchangeService(
    ApplicationDbContext db,
    ApiKeyService apiKeyService,
    IAuditEventService auditEventService,
    ILogger<InfrastructureRunnerEnrollmentExchangeService> logger)
{
    public async Task<RunnerEnrollmentExchangeResponse?> CompleteAsync(
        RunnerEnrollment enrollment,
        RunnerHost host,
        RunnerEnrollmentExchangeRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (host.InfrastructureRunnerId.HasValue)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Failed;
            enrollment.FailureReason = "The target Runner host already has an Infrastructure Runner.";
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("The target Runner host already has an Infrastructure Runner.");
        }

        var runnerName = string.IsNullOrWhiteSpace(request.RunnerName)
            ? $"infrastructure-{host.Name}"
            : request.RunnerName.Trim();
        if (runnerName.Length > 120)
            return null;

        var registration = await CreateRunnerAsync(enrollment, request, runnerName, cancellationToken);
        host.InfrastructureRunnerId = registration.Runner.Id;
        host.InfrastructureRunnerEnrolledAtUtc = now;
        await UpdateHostProvisioningStateAsync(host, cancellationToken);
        host.UpdatedAt = now;
        enrollment.Status = RunnerEnrollmentStatuses.Completed;
        enrollment.UsedAtUtc = now;
        enrollment.RunnerId = registration.Runner.Id;
        await db.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Infrastructure Runner '{registration.Runner.Name}' enrolled successfully.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = enrollment.RequestedByUserId,
            Metadata = new
            {
                enrollment.RunnerHostId,
                enrollment.RunnerId,
                registration.Runner.Name,
                registration.Runner.MachineName,
                registration.Runner.Version,
                registration.Runner.Purpose,
                Capabilities = registration.Capabilities,
                registration.ApiKey.KeyPrefix
            }
        }, cancellationToken);

        logger.LogInformation(
            "Infrastructure Runner enrolled for host {RunnerHostId} as runner {RunnerId}",
            enrollment.RunnerHostId,
            registration.Runner.Id);
        return new RunnerEnrollmentExchangeResponse
        {
            RunnerId = registration.Runner.Id,
            RunnerHostId = enrollment.RunnerHostId,
            RunnerName = registration.Runner.Name,
            ApiKey = registration.RawApiKey,
            Purpose = registration.Runner.Purpose,
            Capabilities = registration.Capabilities,
            InstallationId = enrollment.InstallationId
        };
    }

    private async Task<RunnerRegistration> CreateRunnerAsync(
        RunnerEnrollment enrollment,
        RunnerEnrollmentExchangeRequest request,
        string runnerName,
        CancellationToken cancellationToken)
    {
        var capabilities = RunnerCapabilities.Deserialize(
            enrollment.ExpectedCapabilitiesJson,
            enrollment.ExpectedPurpose);
        var (rawApiKey, apiKey) = await apiKeyService.CreateApiKeyAsync(
            enrollment.RequestedByUserId,
            $"Infrastructure Runner - {runnerName}"[..Math.Min(100, $"Infrastructure Runner - {runnerName}".Length)],
            ApiKeyScope.Runner,
            "Issued by one-time Infrastructure Runner enrollment.",
            saveChanges: false,
            source: ApiKeySource.InfrastructureRunnerEnrollment);
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = runnerName,
            Status = RunnerConnectivityStatuses.Unknown,
            Version = request.RunnerVersion,
            MachineName = request.MachineName,
            Purpose = enrollment.ExpectedPurpose,
            CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, enrollment.ExpectedPurpose),
            InstallationId = enrollment.InstallationId
        };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = enrollment.RequestedByUserId });
        return new RunnerRegistration(runner, apiKey, rawApiKey, capabilities);
    }

    private async Task UpdateHostProvisioningStateAsync(RunnerHost host, CancellationToken cancellationToken)
    {
        if (host.ProvisioningStatus == RunnerHostProvisioningStatuses.Manual)
            return;

        host.ProvisioningStatus = RunnerHostProvisioningStatuses.Validating;
        var operation = await db.RunnerHostProvisioningOperations
            .Where(x => x.RunnerHostId == host.Id
                && x.Status == RunnerHostProvisioningOperationStatuses.AwaitingInfrastructureRunner)
            .OrderByDescending(x => x.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (operation is not null)
            operation.Status = RunnerHostProvisioningOperationStatuses.Running;
    }

    private sealed record RunnerRegistration(
        Runner Runner,
        ApiKey ApiKey,
        string RawApiKey,
        IReadOnlyList<string> Capabilities);
}
