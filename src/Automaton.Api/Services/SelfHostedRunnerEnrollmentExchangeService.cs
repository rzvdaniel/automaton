using System.Data;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Exchanges one-time installation credentials for a managed Runner identity.
/// </summary>
public sealed class SelfHostedRunnerEnrollmentExchangeService(
    ApplicationDbContext db,
    ApiKeyService apiKeyService,
    IAuditEventService auditEventService,
    ILogger<SelfHostedRunnerEnrollmentExchangeService> logger)
{
    public async Task<RunnerEnrollmentExchangeResponse?> ExchangeAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentToken))
            return null;

        if (!db.Database.IsRelational())
            return await ExchangeCoreAsync(request, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var response = await ExchangeCoreAsync(request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private async Task<RunnerEnrollmentExchangeResponse?> ExchangeCoreAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        var candidate = await LoadPendingEnrollmentAsync(request, cancellationToken);
        if (candidate is null)
            return null;

        var registration = await RegisterRunnerAsync(candidate.Enrollment, candidate.ExistingRunner, request, cancellationToken);
        candidate.Enrollment.Status = RunnerEnrollmentStatuses.Completed;
        candidate.Enrollment.UsedAtUtc = candidate.Now;
        candidate.Enrollment.RunnerId = registration.Runner.Id;
        await db.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Succeeded,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = candidate.Enrollment.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCompleted,
            Message = $"Self-hosted Runner '{registration.Runner.Name}' enrolled successfully.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = candidate.Enrollment.UserId,
            Metadata = new
            {
                registration.Runner.Id,
                registration.Runner.Name,
                registration.Runner.Purpose,
                registration.Runner.ApplicationProfile,
                Capabilities = registration.Capabilities
            }
        }, cancellationToken);
        logger.LogInformation(
            "Self-hosted Runner {RunnerId} enrolled from installation {InstallationId}",
            registration.Runner.Id,
            candidate.Enrollment.InstallationId);

        return new RunnerEnrollmentExchangeResponse
        {
            RunnerId = registration.Runner.Id,
            RunnerName = registration.Runner.Name,
            ApiKey = registration.RawApiKey,
            Purpose = registration.Runner.Purpose,
            ApplicationProfile = registration.Runner.ApplicationProfile,
            Capabilities = registration.Capabilities,
            InstallationId = registration.Runner.InstallationId!
        };
    }

    private async Task<EnrollmentCandidate?> LoadPendingEnrollmentAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(request.EnrollmentToken!.Trim());
        var enrollment = await db.SelfHostedRunnerEnrollments
            .FirstOrDefaultAsync(x => x.EnrollmentTokenHash == tokenHash, cancellationToken);
        if (enrollment is null || enrollment.Status != RunnerEnrollmentStatuses.Pending)
            return null;
        if (RunnerApplicationProfiles.Normalize(enrollment.ApplicationProfile) != RunnerApplicationProfiles.MachineManagement)
            return null;

        var now = DateTime.UtcNow;
        if (enrollment.ExpiresAtUtc <= now)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Expired;
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        EnsureRequestedNameMatches(request.RunnerName, enrollment.RunnerName);
        var existingRunner = await db.Runners.Include(runner => runner.RunnerUsers)
            .FirstOrDefaultAsync(runner => runner.InstallationId == enrollment.InstallationId, cancellationToken);
        EnsureRunnerIdentityBelongsToEnrollment(existingRunner, enrollment.UserId);
        return new EnrollmentCandidate(enrollment, existingRunner, now);
    }

    private async Task<RunnerRegistration> RegisterRunnerAsync(
        SelfHostedRunnerEnrollment enrollment,
        Runner? existingRunner,
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        var capabilities = enrollment.ApplicationProfile == RunnerApplicationProfiles.MachineManagement
            ? [RunnerCapabilities.ApplicationHosting, RunnerCapabilities.WorkflowExecution, RunnerCapabilities.ManagedContainerExecution]
            : RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed);
        var (rawApiKey, apiKey) = await apiKeyService.CreateApiKeyAsync(
            enrollment.UserId,
            $"Self-hosted Runner - {enrollment.RunnerName}",
            ApiKeyScope.Runner,
            "Issued after one-time self-hosted Runner enrollment.",
            saveChanges: false,
            source: ApiKeySource.RunnerProvisioning);
        var runner = existingRunner ?? new Runner { Id = Guid.NewGuid(), InstallationId = enrollment.InstallationId };
        runner.Name = enrollment.RunnerName;
        runner.Status = RunnerConnectivityStatuses.Unknown;
        runner.Version = request.RunnerVersion;
        runner.MachineName = request.MachineName;
        runner.Purpose = RunnerPurposes.Managed;
        runner.ApplicationProfile = enrollment.ApplicationProfile;
        runner.CapabilitiesJson = RunnerCapabilities.Serialize(capabilities, RunnerPurposes.Managed);
        runner.ApiKeyId = apiKey.Id;
        if (existingRunner is null)
        {
            db.Runners.Add(runner);
            db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = enrollment.UserId });
        }

        return new RunnerRegistration(runner, rawApiKey, capabilities);
    }

    private static void EnsureRequestedNameMatches(string? requestedName, string enrolledName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName)
            && !string.Equals(requestedName.Trim(), enrolledName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Runner name does not match the self-hosted installation enrollment.");
        }
    }

    private static void EnsureRunnerIdentityBelongsToEnrollment(Runner? existingRunner, Guid userId)
    {
        if (existingRunner is not null
            && (!existingRunner.RunnerUsers.Any(user => user.UserId == userId)
                || !string.Equals(existingRunner.Purpose, RunnerPurposes.Managed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The self-hosted installation identity belongs to another Runner.");
        }
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private sealed record EnrollmentCandidate(
        SelfHostedRunnerEnrollment Enrollment,
        Runner? ExistingRunner,
        DateTime Now);

    private sealed record RunnerRegistration(
        Runner Runner,
        string RawApiKey,
        IReadOnlyList<string> Capabilities);
}
