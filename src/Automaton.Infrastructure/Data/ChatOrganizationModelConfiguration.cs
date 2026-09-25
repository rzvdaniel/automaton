using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class ChatOrganizationModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureOrganization(modelBuilder);
        ConfigureOrganizationMember(modelBuilder);
        ConfigureOrganizationAppAssignment(modelBuilder);
        ConfigureOrganizationInvitation(modelBuilder);
    }

    private static void ConfigureOrganization(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<Organization>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Slug).HasMaxLength(100);
                entity.Property(e => e.OwnerId).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.AllowPublicSignup).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.RequireEmailDomain).HasMaxLength(255);
                entity.Property(e => e.LogoUrl).HasMaxLength(500);
                entity.Property(e => e.PrimaryColor).HasMaxLength(7);

                // Slug must be unique
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.HasIndex(e => e.OwnerId);
                entity.HasIndex(e => new { e.OwnerId, e.IsActive });

                // Configure relationship with Owner
                entity.HasOne(e => e.Owner)
                    .WithMany(u => u.OwnedOrganizations)
                    .HasForeignKey(e => e.OwnerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureOrganizationMember(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<OrganizationMember>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrganizationId).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.Role).IsRequired();
                entity.Property(e => e.JoinedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

                // Unique constraint: one membership per user per organization
                entity.HasIndex(e => new { e.OrganizationId, e.UserId }).IsUnique();
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => new { e.OrganizationId, e.UserId, e.IsActive });

                // Configure relationships
                entity.HasOne(e => e.Organization)
                    .WithMany(o => o.Members)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.OrganizationMemberships)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Inviter)
                    .WithMany()
                    .HasForeignKey(e => e.InvitedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureOrganizationAppAssignment(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<OrganizationAppAssignment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrganizationId).IsRequired();
                entity.Property(e => e.AppId).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.AssignedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

                // Unique constraint: one assignment per user per app per organization
                entity.HasIndex(e => new { e.OrganizationId, e.AppId, e.UserId }).IsUnique();
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => e.AppId);
                entity.HasIndex(e => e.UserId);

                // Configure relationships
                entity.HasOne(e => e.Organization)
                    .WithMany(o => o.AppAssignments)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.App)
                    .WithMany()
                    .HasForeignKey(e => e.AppId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Assigner)
                    .WithMany()
                    .HasForeignKey(e => e.AssignedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureOrganizationInvitation(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<OrganizationInvitation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrganizationId).IsRequired();
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Role).IsRequired();
                entity.Property(e => e.Token).IsRequired().HasMaxLength(64);
                entity.Property(e => e.InvitedBy).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
                entity.Property(e => e.IsConsumed).IsRequired().HasDefaultValue(false);

                // Token must be unique
                entity.HasIndex(e => e.Token).IsUnique();
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => e.Email);
                entity.HasIndex(e => new { e.OrganizationId, e.Email, e.IsConsumed });

                // Configure relationships
                entity.HasOne(e => e.Organization)
                    .WithMany(o => o.Invitations)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Inviter)
                    .WithMany()
                    .HasForeignKey(e => e.InvitedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }
}
