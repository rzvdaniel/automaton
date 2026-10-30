using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class UserHardDeleteService(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    IAuditEventService auditEventService)
{
    public async Task DeleteAsync(Guid userId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new InvalidOperationException($"User with userId {userId} not found!");
        }

        var auditMetadata = new
        {
            user.Email,
            user.UserName,
            user.Name,
            user.PublicUsername,
            user.RegisteredAtUtc
        };

        var blockers = await GetHardDeleteBlockersAsync(userId, cancellationToken);
        if (blockers.Count > 0)
        {
            var blockerSummary = string.Join(
                Environment.NewLine,
                blockers.Select(blocker => $"- {blocker.Count} {blocker.Label}"));
            throw new InvalidOperationException(
                $"Cannot delete this user.{Environment.NewLine}{Environment.NewLine}" +
                $"This account still owns:{Environment.NewLine}{Environment.NewLine}" +
                $"{blockerSummary}{Environment.NewLine}{Environment.NewLine}" +
                "Disable the account instead, or remove or transfer these resources first.");
        }

        await RemoveOwnedIdentityDataAsync(userId, cancellationToken);
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errorMessage = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException(
                $"Hard delete failed for user {user.Email ?? user.Id.ToString()}: {errorMessage}");
        }

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.User,
            EntityId = userId,
            EventType = AuditEventTypes.UserDeleted,
            Message = $"User '{user.Email ?? user.UserName ?? userId.ToString()}' was deleted",
            Outcome = AuditOutcomes.Success,
            ActorUserId = actorUserId,
            TargetUserId = userId,
            Metadata = auditMetadata
        }, cancellationToken);
    }

    private async Task RemoveOwnedIdentityDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        var refreshTokens = await db.RefreshTokens.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var userGitHubTokens = await db.UserGitHubTokens.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var userSettings = await db.UserSettings.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var recentActivities = await db.RecentActivities.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var userClaims = await db.Set<IdentityUserClaim<Guid>>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var userLogins = await db.Set<IdentityUserLogin<Guid>>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var userTokens = await db.Set<IdentityUserToken<Guid>>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var userRoles = await db.Set<ApplicationUserRole>().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var apiKeyLinks = await db.ApiKeyUsers.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var ownedApiKeyIds = apiKeyLinks.Select(item => item.ApiKeyId).Distinct().ToList();
        var ownedApiKeys = await db.ApiKeys.Where(item => ownedApiKeyIds.Contains(item.Id)).ToListAsync(cancellationToken);

        db.RefreshTokens.RemoveRange(refreshTokens);
        db.UserGitHubTokens.RemoveRange(userGitHubTokens);
        db.UserSettings.RemoveRange(userSettings);
        db.RecentActivities.RemoveRange(recentActivities);
        db.ApiKeyUsers.RemoveRange(apiKeyLinks);
        db.ApiKeys.RemoveRange(ownedApiKeys);
        db.RemoveRange(userClaims);
        db.RemoveRange(userLogins);
        db.RemoveRange(userTokens);
        db.RemoveRange(userRoles);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<(string Label, int Count)>> GetHardDeleteBlockersAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var blockers = new List<(string Label, int Count)>();
        await AddBlockerAsync(blockers, "apps", db.WebAppUsers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "flows", db.FlowUsers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "runners", db.RunnerUsers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "custom steps", db.CustomStepUsers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "schedules", db.ScheduleUsers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "sensitive variables", db.SensitiveVariableUsers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "project memberships", db.ProjectMembers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "organization memberships", db.OrganizationMembers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "organization app assignments", db.OrganizationAppAssignments.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "chat sessions", db.AiChatSessionUsers.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "hosted runners", db.HostedRunners.AsNoTracking().CountAsync(item => item.UserId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "account storage allocations", db.AccountStorageAllocations.AsNoTracking().CountAsync(item => item.AccountId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "owned projects", db.Projects.AsNoTracking().CountAsync(item => item.OwnerId == userId, cancellationToken));
        await AddBlockerAsync(blockers, "owned organizations", db.Organizations.AsNoTracking().CountAsync(item => item.OwnerId == userId, cancellationToken));
        return blockers;
    }

    private static async Task AddBlockerAsync(
        ICollection<(string Label, int Count)> blockers,
        string label,
        Task<int> countTask)
    {
        var count = await countTask;
        if (count > 0)
        {
            blockers.Add((label, count));
        }
    }
}
