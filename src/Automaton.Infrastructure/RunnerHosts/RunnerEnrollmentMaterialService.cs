using System.Security.Cryptography;
using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Infrastructure.RunnerHosts;

public sealed record RunnerEnrollmentMaterial(
    RunnerEnrollment Enrollment,
    string PlaintextToken);

public sealed class RunnerEnrollmentMaterialService(ApplicationDbContext db)
{
    public async Task<RunnerEnrollmentMaterial> CreateAsync(
        Guid runnerHostId,
        Guid requestedByUserId,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        var host = await db.RunnerHosts.FirstOrDefaultAsync(x => x.Id == runnerHostId, cancellationToken)
            ?? throw new KeyNotFoundException("Runner host was not found.");
        if (host.InfrastructureRunnerId.HasValue)
            throw new InvalidOperationException("The Runner host already has an Infrastructure Runner.");

        var pending = await db.RunnerEnrollments
            .Where(x => x.RunnerHostId == runnerHostId && x.Status == RunnerEnrollmentStatuses.Pending)
            .ToListAsync(cancellationToken);
        foreach (var existing in pending)
        {
            existing.Status = RunnerEnrollmentStatuses.Revoked;
            existing.FailureReason = "Superseded by a new provisioning attempt.";
        }

        var token = $"re_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
        var now = DateTime.UtcNow;
        var enrollment = new RunnerEnrollment
        {
            Id = Guid.NewGuid(),
            RunnerHostId = runnerHostId,
            RequestedByUserId = requestedByUserId,
            ExpectedPurpose = RunnerPurposes.Infrastructure,
            ExpectedCapabilitiesJson = RunnerCapabilities.Serialize(
                RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                RunnerPurposes.Infrastructure),
            EnrollmentTokenHash = HashToken(token),
            InstallationId = $"ri_{Guid.NewGuid():N}",
            Status = RunnerEnrollmentStatuses.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(lifetime)
        };
        db.RunnerEnrollments.Add(enrollment);
        await db.SaveChangesAsync(cancellationToken);
        return new(enrollment, token);
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
