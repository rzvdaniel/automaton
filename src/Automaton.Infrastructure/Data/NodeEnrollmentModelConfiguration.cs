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
    }
}
