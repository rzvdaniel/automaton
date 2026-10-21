using System.Data;
using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class RunnerEnrollmentService(
    ApplicationDbContext db,
    InfrastructureRunnerEnrollmentExchangeService infrastructureRunnerEnrollmentExchangeService,
    RunnerEnrollmentAdministrationService administrationService)
{
    public Task<RunnerEnrollmentCreatedResponse> CreateAsync(
        Guid runnerHostId,
        Guid requestedByUserId,
        CancellationToken cancellationToken) =>
        administrationService.CreateAsync(runnerHostId, requestedByUserId, cancellationToken);

    public async Task<RunnerEnrollmentExchangeResponse?> ExchangeAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EnrollmentToken))
        {
            return null;
        }

        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var response = await ExchangeCoreAsync(request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }

        return await ExchangeCoreAsync(request, cancellationToken);
    }

    /// <summary>
    /// Validates a fresh Infrastructure Runner enrollment token without consuming it.
    /// This is used only for pre-enrollment bootstrap material; the normal exchange
    /// path remains responsible for creating the Runner and completing the enrollment.
    /// </summary>
    public async Task<bool> IsInfrastructureEnrollmentTokenValidAsync(
        string enrollmentToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(enrollmentToken))
        {
            return false;
        }

        var tokenHash = HashToken(enrollmentToken.Trim());
        var enrollment = await db.RunnerEnrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EnrollmentTokenHash == tokenHash,
                cancellationToken);

        if (enrollment == null
            || enrollment.Status != RunnerEnrollmentStatuses.Pending
            || enrollment.ExpectedPurpose != RunnerPurposes.Infrastructure
            || enrollment.HostedRunnerId.HasValue
            || enrollment.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return false;
        }

        var host = await db.RunnerHosts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == enrollment.RunnerHostId,
                cancellationToken);

        return host != null && !host.InfrastructureRunnerId.HasValue;
    }

    public Task<bool> RevokeAsync(
        Guid runnerHostId,
        Guid enrollmentId,
        Guid requestedByUserId,
        CancellationToken cancellationToken) =>
        administrationService.RevokeAsync(runnerHostId, enrollmentId, requestedByUserId, cancellationToken);

    private async Task<RunnerEnrollmentExchangeResponse?> ExchangeCoreAsync(
        RunnerEnrollmentExchangeRequest request,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(request.EnrollmentToken.Trim());
        var enrollment = await db.RunnerEnrollments
            .FirstOrDefaultAsync(x => x.EnrollmentTokenHash == tokenHash, cancellationToken);

        if (enrollment == null || enrollment.Status != RunnerEnrollmentStatuses.Pending)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (enrollment.ExpiresAtUtc <= now)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Expired;
            enrollment.FailureReason = "Enrollment token expired.";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        var host = await db.RunnerHosts
            .FirstOrDefaultAsync(x => x.Id == enrollment.RunnerHostId, cancellationToken);

        if (host == null)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Failed;
            enrollment.FailureReason = "The target Runner host no longer exists.";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (enrollment.HostedRunnerId.HasValue)
        {
            enrollment.Status = RunnerEnrollmentStatuses.Failed;
            enrollment.FailureReason = "Per-user Hosted Runner enrollment is no longer supported. Assign a Management Runner instead.";
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        return await infrastructureRunnerEnrollmentExchangeService.CompleteAsync(
            enrollment, host, request, now, cancellationToken);
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
