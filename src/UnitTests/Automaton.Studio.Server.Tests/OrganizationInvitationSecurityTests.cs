using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class OrganizationInvitationSecurityTests
{
    [Test]
    public async Task AcceptInvitationAsync_ReturnsFalse_WhenAuthenticatedUserEmailDoesNotMatchInvitation()
    {
        var ownerId = Guid.NewGuid();
        var invitedUserId = Guid.NewGuid();
        var attackerUserId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        const string token = "org-invite-token";

        await using var dbContext = CreateDbContext();
        SeedUsersAndOrganization(dbContext, ownerId, invitedUserId, attackerUserId, organizationId);

        dbContext.OrganizationInvitations.Add(new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = "invited@example.com",
            Role = OrganizationRole.Developer,
            Token = token,
            InvitedBy = ownerId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsConsumed = false
        });
        await dbContext.SaveChangesAsync();

        var service = CreateOrganizationService(dbContext, ownerId);

        var accepted = await service.AcceptInvitationAsync(token, attackerUserId, CancellationToken.None);

        Assert.That(accepted, Is.False);
        Assert.That(await dbContext.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == attackerUserId), Is.False);
        Assert.That(await dbContext.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == invitedUserId), Is.False);
    }

    [Test]
    public async Task AcceptInvitationAsync_AddsMembership_WhenAuthenticatedUserEmailMatchesInvitation()
    {
        var ownerId = Guid.NewGuid();
        var invitedUserId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        const string token = "org-invite-token";

        await using var dbContext = CreateDbContext();
        SeedUsersAndOrganization(dbContext, ownerId, invitedUserId, Guid.NewGuid(), organizationId);

        dbContext.OrganizationInvitations.Add(new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = "invited@example.com",
            Role = OrganizationRole.Developer,
            Token = token,
            InvitedBy = ownerId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsConsumed = false
        });
        await dbContext.SaveChangesAsync();

        var service = CreateOrganizationService(dbContext, ownerId);

        var accepted = await service.AcceptInvitationAsync(token, invitedUserId, CancellationToken.None);

        Assert.That(accepted, Is.True);
        Assert.That(await dbContext.OrganizationMembers.AnyAsync(m =>
            m.OrganizationId == organizationId &&
            m.UserId == invitedUserId &&
            m.IsActive &&
            m.Role == OrganizationRole.Developer), Is.True);

        var invitation = await dbContext.OrganizationInvitations.SingleAsync(i => i.Token == token);
        Assert.That(invitation.IsConsumed, Is.True);
        Assert.That(invitation.AcceptedAt, Is.Not.Null);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static OrganizationService CreateOrganizationService(ApplicationDbContext dbContext, Guid currentUserId)
    {
        var userContextService = new UserContextService(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        });
        userContextService.ImpersonateUser(currentUserId);

        return new OrganizationService(
            dbContext,
            userContextService,
            null!,
            null!,
            null!,
            null!,
            new NoOpAuditEventService());
    }

    private static void SeedUsersAndOrganization(
        ApplicationDbContext dbContext,
        Guid ownerId,
        Guid invitedUserId,
        Guid attackerUserId,
        Guid organizationId)
    {
        dbContext.Users.AddRange(
            CreateUser(ownerId, "owner@example.com", "owner"),
            CreateUser(invitedUserId, "invited@example.com", "invited"),
            CreateUser(attackerUserId, "attacker@example.com", "attacker"));

        dbContext.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = "Org",
            OwnerId = ownerId,
            Slug = "org",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });

        dbContext.SaveChanges();
    }

    private static ApplicationUser CreateUser(Guid id, string email, string publicUsername)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            Email = email,
            Name = publicUsername,
            PublicUsername = publicUsername
        };
    }

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(
            AuditEventWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task WriteAsync(
            string action,
            string entityType,
            Guid? entityId,
            string message,
            object? metadata = null,
            string outcome = "Success",
            bool failOnError = false,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
