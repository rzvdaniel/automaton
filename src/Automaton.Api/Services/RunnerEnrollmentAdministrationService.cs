using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>
/// Creates and revokes administrator-requested Infrastructure Runner enrollments.
/// </summary>
public sealed class RunnerEnrollmentAdministrationService(
    ApplicationDbContext db,
    IAuditEventService auditEventService,
    IOptions<RunnerEnrollmentOptions> options,
    IOptions<HostedRunnerOptions> hostedRunnerOptions)
{
    public async Task<RunnerEnrollmentCreatedResponse> CreateAsync(
        Guid runnerHostId,
        Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
        var host = await LoadAvailableHostAsync(runnerHostId, cancellationToken);
        await RevokePendingEnrollmentsAsync(runnerHostId, cancellationToken);
        var now = DateTime.UtcNow;
        var enrollmentToken = GenerateEnrollmentToken();
        var enrollment = CreateEnrollment(host.Id, requestedByUserId, enrollmentToken, now);

        db.RunnerEnrollments.Add(enrollment);
        await db.SaveChangesAsync(cancellationToken);
        await WriteCreatedAuditAsync(host, enrollment, cancellationToken);
        return CreateResponse(enrollment, enrollmentToken);
    }

    public async Task<bool> RevokeAsync(
        Guid runnerHostId,
        Guid enrollmentId,
        Guid requestedByUserId,
        CancellationToken cancellationToken)
    {
        var enrollment = await db.RunnerEnrollments
            .FirstOrDefaultAsync(x => x.Id == enrollmentId && x.RunnerHostId == runnerHostId, cancellationToken);
        if (enrollment is null)
            return false;

        if (enrollment.Status == RunnerEnrollmentStatuses.Pending)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Revoked;
            enrollment.FailureReason = "Revoked by an administrator.";
            await db.SaveChangesAsync(cancellationToken);
            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Updated,
                EntityType = AuditEntityTypes.RunnerEnrollment,
                EntityId = enrollment.Id,
                EventType = AuditEventTypes.RunnerEnrollmentRevoked,
                Message = "Infrastructure Runner enrollment revoked.",
                Outcome = AuditOutcomes.Success,
                ActorUserId = requestedByUserId,
                Metadata = new { enrollment.RunnerHostId }
            }, cancellationToken);
        }

        return true;
    }

    private async Task<RunnerHost> LoadAvailableHostAsync(Guid runnerHostId, CancellationToken cancellationToken)
    {
        var host = await db.RunnerHosts.FirstOrDefaultAsync(x => x.Id == runnerHostId, cancellationToken)
            ?? throw new KeyNotFoundException("Runner host was not found.");
        if (host.InfrastructureRunnerId.HasValue)
            throw new InvalidOperationException("The Runner host already has an Infrastructure Runner.");
        return host;
    }

    private async Task RevokePendingEnrollmentsAsync(
        Guid runnerHostId,
        CancellationToken cancellationToken)
    {
        var pending = await db.RunnerEnrollments
            .Where(x => x.RunnerHostId == runnerHostId && x.Status == RunnerEnrollmentStatuses.Pending)
            .ToListAsync(cancellationToken);
        foreach (var existing in pending)
        {
            existing.Status = RunnerEnrollmentStatuses.Revoked;
            existing.FailureReason = "Superseded by a new enrollment.";
        }
    }

    private RunnerEnrollment CreateEnrollment(
        Guid runnerHostId,
        Guid requestedByUserId,
        string enrollmentToken,
        DateTime now)
    {
        var expectedCapabilities = RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure);
        return new RunnerEnrollment
        {
            Id = Guid.NewGuid(),
            RunnerHostId = runnerHostId,
            RequestedByUserId = requestedByUserId,
            ExpectedPurpose = RunnerPurposes.Infrastructure,
            ExpectedCapabilitiesJson = RunnerCapabilities.Serialize(expectedCapabilities, RunnerPurposes.Infrastructure),
            EnrollmentTokenHash = HashToken(enrollmentToken),
            InstallationId = $"ri_{Guid.NewGuid():N}",
            Status = RunnerEnrollmentStatuses.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(Math.Clamp(options.Value.TokenLifetimeMinutes, 1, 60))
        };
    }

    private Task WriteCreatedAuditAsync(
        RunnerHost host,
        RunnerEnrollment enrollment,
        CancellationToken cancellationToken)
    {
        var expectedCapabilities = RunnerCapabilities.Deserialize(
            enrollment.ExpectedCapabilitiesJson,
            enrollment.ExpectedPurpose);
        return auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.RunnerEnrollment,
            EntityId = enrollment.Id,
            EventType = AuditEventTypes.RunnerEnrollmentCreated,
            Message = $"Infrastructure Runner enrollment created for host '{host.Name}'.",
            Outcome = AuditOutcomes.Success,
            ActorUserId = enrollment.RequestedByUserId,
            Metadata = new
            {
                enrollment.RunnerHostId,
                enrollment.ExpiresAtUtc,
                enrollment.ExpectedPurpose,
                ExpectedCapabilities = expectedCapabilities
            }
        }, cancellationToken);
    }

    private RunnerEnrollmentCreatedResponse CreateResponse(RunnerEnrollment enrollment, string enrollmentToken)
        => new()
        {
            Id = enrollment.Id,
            RunnerHostId = enrollment.RunnerHostId,
            EnrollmentToken = enrollmentToken,
            ExpiresAtUtc = enrollment.ExpiresAtUtc,
            ExpectedPurpose = enrollment.ExpectedPurpose,
            ExpectedCapabilities = RunnerCapabilities.Deserialize(
                enrollment.ExpectedCapabilitiesJson,
                enrollment.ExpectedPurpose),
            DockerImage = hostedRunnerOptions.Value.DockerImage
        };

    private static string GenerateEnrollmentToken()
        => $"re_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}"
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
