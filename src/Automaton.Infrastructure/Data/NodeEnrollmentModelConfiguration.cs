using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class NodeEnrollmentModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeEnrollmentAuthorization>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.InstallationId).IsRequired();
            entity.Property(value => value.OwnerScope).IsRequired().HasMaxLength(128);
            entity.Property(value => value.PinnedPublicKeySha256).IsRequired().HasMaxLength(64);
            entity.Property(value => value.EnrollmentTokenSha256).IsRequired().HasMaxLength(64);
            entity.Property(value => value.Status).IsRequired().HasMaxLength(24);
            entity.Property(value => value.CertificateSha256).HasMaxLength(64);
            entity.Property(value => value.NodeApiKeySha256).HasMaxLength(64);
            entity.HasIndex(value => value.InstallationId).IsUnique();
            entity.HasIndex(value => value.NodeId).IsUnique();
            entity.HasIndex(value => value.EnrollmentTokenSha256).IsUnique();
            entity.HasIndex(value => new { value.Id, value.Status });
        });

        modelBuilder.Entity<NodeEnrollmentRecoveryChallenge>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Nonce).IsRequired().HasMaxLength(32);
            entity.Property(value => value.PublicKeySha256).IsRequired().HasMaxLength(64);
            entity.HasIndex(value => new { value.EnrollmentAuthorizationId, value.ExpiresAtUtc });
            entity.HasOne(value => value.EnrollmentAuthorization)
                .WithMany(value => value.RecoveryChallenges)
                .HasForeignKey(value => value.EnrollmentAuthorizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NodeManagement>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_NodeManagement_PositiveFenceState",
                "CredentialRevision > 0 AND RecoveryEpoch > 0 AND (CurrentIncarnation IS NULL OR CurrentIncarnation > 0) AND HostFenceGeneration >= 0"));
            entity.HasKey(value => value.NodeId);
            entity.Property(value => value.Status).IsRequired().HasMaxLength(24);
            entity.Property(value => value.RecoveryCheckpointSha256).HasMaxLength(64);
            entity.HasIndex(value => value.InstallationId).IsUnique();
            entity.HasIndex(value => value.EnrollmentAuthorizationId).IsUnique();
            entity.HasOne<NodeEnrollmentAuthorization>()
                .WithOne()
                .HasForeignKey<NodeManagement>(value => value.EnrollmentAuthorizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NodeActivationAttempt>(entity =>
        {
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_NodeActivationAttempts_PositiveState",
                "RecoveryEpoch > 0 AND CredentialRevision > 0 AND AssignedIncarnation > 0 AND (ExpectedIncarnation IS NULL OR ExpectedIncarnation > 0)"));
            entity.HasKey(value => value.Id);
            entity.Property(value => value.RequestSha256).IsRequired().HasMaxLength(64);
            entity.Property(value => value.Status).IsRequired().HasMaxLength(24);
            entity.Property(value => value.ImmutableReceiptJson).IsRequired().HasColumnType("longblob");
            entity.HasIndex(value => new { value.NodeId, value.ActivationRequestId }).IsUnique();
            entity.HasIndex(value => value.HostFenceReceiptId).IsUnique();
            entity.HasOne(value => value.NodeManagement)
                .WithMany(value => value.ActivationAttempts)
                .HasForeignKey(value => value.NodeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NodeHostFenceReceiptUse>(entity =>
        {
            entity.HasKey(value => value.ReceiptId);
            entity.HasIndex(value => new { value.NodeId, value.HostFenceGeneration }).IsUnique();
            entity.HasIndex(value => new { value.NodeId, value.ActivationRequestId }).IsUnique();
        });
    }
}
