using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Automaton.Api.Models;
using Automaton.Api.Enums;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Entities;
using Automaton.Api.Constants;
using Automaton.Api.Data;

namespace Automaton.Api.Services;

public class UsersService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ApplicationDbContext _db;
    private readonly UserContextService _userContextService;
    private readonly IAuditEventService _auditEventService;
    private readonly UserHardDeleteService _hardDeleteService;

    private Guid ActorUserId => _userContextService.GetUserId();

    public UsersService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext db,
        UserContextService userContextService,
        IAuditEventService auditEventService,
        UserHardDeleteService hardDeleteService)
    {
        _userManager         = userManager;
        _roleManager         = roleManager;
        _db                  = db;
        _userContextService  = userContextService;
        _auditEventService   = auditEventService;
        _hardDeleteService   = hardDeleteService;
    }

    public async Task CreateUser(ApplicationUser user, string password)
    {
        EnsureRegistrationMetadata(user, RegistrationProviders.Password);

        if (_userManager.Users.Any(u => u.UserName == user.UserName))
        {
            throw new Exception($"User with user name {user.UserName} already registered");
        }

        if (_userManager.Users.Any(u => u.Email == user.Email))
        {
            throw new Exception($"User with email address {user.Email} already registered");
        }

        var result = await _userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var exceptionText = result.Errors.Aggregate(
                "User Creation Failed - Identity Exception. Errors were: \n\r\n\r",
                (current, error) => current + (" - " + error.Description + "\n\r"));
            throw new Exception(exceptionText);
        }

        // Assign default subscription plan allowance
        await AssignDefaultPlanAsync(user.Id);
    }

    public async Task CreateUserWithoutPasswordAsync(ApplicationUser user)
    {
        EnsureRegistrationMetadata(user, RegistrationProviders.GitHub);

        if (_userManager.Users.Any(u => u.UserName == user.UserName))
        {
            throw new Exception($"User with user name {user.UserName} already registered");
        }

        if (_userManager.Users.Any(u => u.Email == user.Email))
        {
            throw new Exception($"User with email address {user.Email} already registered");
        }

        var result = await _userManager.CreateAsync(user);

        if (!result.Succeeded)
        {
            var exceptionText = result.Errors.Aggregate(
                "User Creation Failed - Identity Exception. Errors were: \n\r\n\r",
                (current, error) => current + (" - " + error.Description + "\n\r"));
            throw new Exception(exceptionText);
        }

        await AssignDefaultPlanAsync(user.Id);
    }

    public async Task UpdatePassword(Guid userId, string oldPassword, string newPassword)
    {
        var user = await GetUserById(userId);

        var result = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        if (!result.Succeeded)
        {
            throw new Exception("Error in updating password!");
        }
    }

    public async Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var refreshTokens = await _db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (refreshTokens.Count == 0)
        {
            return;
        }

        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.Revoke();
        }

        _db.RefreshTokens.UpdateRange(refreshTokens);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateProfile(UserProfile userProfile)
    {
        var user = await GetUserById(userProfile.Id);

        user.Name = userProfile.Name;
        user.Email = userProfile.Email;     

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            throw new Exception("Error in updating Profile!");
        }
    }

    /// <summary>
    /// Update user's public username
    /// </summary>
    public async Task UpdatePublicUsernameAsync(Guid userId, string newPublicUsername)
    {
        var user = await GetUserById(userId);
        user.PublicUsername = newPublicUsername;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            throw new Exception("Error in updating public username!");
        }
    }

    public async Task<ApplicationUser> GetUserById(Guid userId)
    {
        var result = await _userManager.FindByIdAsync(userId.ToString());
        if (result == null)
            throw new Exception($"User with userId {userId} not found!");

        if (result.IsDeleted)
            throw new Exception($"User with userId {userId} not found!");

        return result;
    }

    public async Task<ApplicationUser> GetUserByEmailOrUserName(string emailOrUserName)
    {
        var result = await _userManager.FindByEmailAsync(emailOrUserName) ??
                     await _userManager.FindByNameAsync(emailOrUserName);
        if (result == null)
            throw new Exception($"User with Email or UserName '{emailOrUserName}' not found!");

        return result;
    }

    public async Task<ApplicationUser?> FindByEmailAsync(string email)
    {
        return await _userManager.FindByEmailAsync(email);
    }

    public async Task<ApplicationUser?> FindByEmailOrUserNameAsync(string emailOrUserName)
    {
        return await _userManager.FindByEmailAsync(emailOrUserName) ??
               await _userManager.FindByNameAsync(emailOrUserName);
    }

    public async Task<ApplicationUser?> FindByIdIncludingDeletedAsync(Guid userId)
    {
        return await _userManager.FindByIdAsync(userId.ToString());
    }

    public async Task<bool> ValidateCredentialsAsync(string usernameOrEmail, string password)
    {
        var user = await _userManager.FindByEmailAsync(usernameOrEmail) ??
                   await _userManager.FindByNameAsync(usernameOrEmail);

        if (user != null && !user.IsDeleted)
        {
            return await _userManager.CheckPasswordAsync(user, password);
        }

        return false;
    }

    public async Task<bool> ValidatePasswordAsync(ApplicationUser user, string password)
    {
        if (user != null)
        {
            return await _userManager.CheckPasswordAsync(user, password);
        }

        return false;
    }

    public async Task<ApplicationUserPageResult> GetUsers(UserFilter filter)
    {
        var safePageIndex = Math.Max(0, filter.PageIndex);
        var safePageSize = filter.PageSize <= 0 ? 100 : filter.PageSize;
        var now = DateTimeOffset.UtcNow;
        var todayUtc = DateTime.UtcNow.Date;
        var weekStartUtc = todayUtc.AddDays(-7);
        var monthStartUtc = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var query = UserListingQuery.Create(_userManager.Users, filter, now);
        var summary = await UserListingQuery.GetSummaryAsync(query, now, todayUtc, weekStartUtc, monthStartUtc);
        var items = await UserListingQuery.GetPageAsync(query, filter, safePageIndex, safePageSize);

        return new ApplicationUserPageResult
        {
            Items = items,
            TotalCount = summary.TotalCount,
            Page = safePageIndex + 1,
            PageSize = safePageSize,
            Summary = summary,
        };
    }

    public async Task<Dictionary<Guid, string>> ResolveRegistrationProvidersAsync(
        IEnumerable<ApplicationUser> users,
        CancellationToken cancellationToken = default)
    {
        var userList = users.ToList();
        var unresolvedUserIds = userList
            .Where(user => string.IsNullOrWhiteSpace(user.RegistrationProvider))
            .Select(user => user.Id)
            .ToList();

        var externalLoginProviders = new Dictionary<Guid, string>();
        if (unresolvedUserIds.Count > 0)
        {
            externalLoginProviders = await _db.Set<IdentityUserLogin<Guid>>()
                .AsNoTracking()
                .Where(login => unresolvedUserIds.Contains(login.UserId))
                .GroupBy(login => login.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    LoginProvider = group.Select(item => item.LoginProvider).FirstOrDefault(),
                })
                .Where(item => item.LoginProvider != null)
                .ToDictionaryAsync(item => item.UserId, item => item.LoginProvider!, cancellationToken);

            if (externalLoginProviders.Count > 0 || unresolvedUserIds.Count > 0)
            {
                await BackfillRegistrationProvidersAsync(userList, externalLoginProviders, cancellationToken);
            }
        }

        return userList.ToDictionary(
            user => user.Id,
            user => ResolveRegistrationProvider(user, externalLoginProviders));
    }

    public async Task MarkSuccessfulLoginAsync(ApplicationUser user)
    {
        if (user.IsDeleted)
        {
            throw new Exception("Invalid credentials.");
        }

        user.LastLoginAtUtc = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new Exception("Failed to update last login timestamp.");
        }
    }

    private static void EnsureRegistrationMetadata(ApplicationUser user, string registrationProvider)
    {
        if (user.RegisteredAtUtc == default)
        {
            user.RegisteredAtUtc = DateTime.UtcNow;
        }

        if (string.IsNullOrWhiteSpace(user.RegistrationProvider))
        {
            user.RegistrationProvider = registrationProvider;
        }
    }

    private static string ResolveRegistrationProvider(
        ApplicationUser user,
        IReadOnlyDictionary<Guid, string> externalLoginProviders)
    {
        if (!string.IsNullOrWhiteSpace(user.RegistrationProvider))
        {
            return NormalizeRegistrationProvider(user.RegistrationProvider);
        }

        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return RegistrationProviders.Password;
        }

        if (externalLoginProviders.TryGetValue(user.Id, out var loginProvider) && !string.IsNullOrWhiteSpace(loginProvider))
        {
            return NormalizeRegistrationProvider(loginProvider);
        }

        return RegistrationProviders.Password;
    }

    private async Task BackfillRegistrationProvidersAsync(
        IReadOnlyCollection<ApplicationUser> users,
        IReadOnlyDictionary<Guid, string> externalLoginProviders,
        CancellationToken cancellationToken)
    {
        var unresolvedUserIds = users
            .Where(user => string.IsNullOrWhiteSpace(user.RegistrationProvider))
            .Select(user => user.Id)
            .ToList();

        if (unresolvedUserIds.Count == 0)
        {
            return;
        }

        var trackedUsers = await _userManager.Users
            .Where(user => unresolvedUserIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        if (trackedUsers.Count == 0)
        {
            return;
        }

        foreach (var trackedUser in trackedUsers)
        {
            trackedUser.RegistrationProvider = ResolveRegistrationProvider(trackedUser, externalLoginProviders);
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.RegistrationProvider) &&
                trackedUsers.FirstOrDefault(trackedUser => trackedUser.Id == user.Id) is { } persistedUser)
            {
                user.RegistrationProvider = persistedUser.RegistrationProvider;
            }
        }
    }

    private static string NormalizeRegistrationProvider(string provider)
    {
        var normalizedProvider = provider.Trim();
        if (normalizedProvider.Length == 0)
        {
            return RegistrationProviders.Password;
        }

        return normalizedProvider.ToLowerInvariant() switch
        {
            "password" => RegistrationProviders.Password,
            "github" => RegistrationProviders.GitHub,
            "google" => RegistrationProviders.Google,
            "microsoft" => RegistrationProviders.Microsoft,
            "invitation" => RegistrationProviders.Invitation,
            "admincreated" => RegistrationProviders.AdminCreated,
            _ => normalizedProvider,
        };
    }

    public async Task<string> GetResetPasswordToken(Guid userId)
    {
        var user = await GetUserById(userId);
        if (user != null)
        {
            return await _userManager.GeneratePasswordResetTokenAsync(user);
        }

        throw new Exception();
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(Guid userId)
    {
        var user = await GetUserById(userId);
        return await _userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<IdentityResult> ConfirmEmailAsync(Guid userId, string token)
    {
        var user = await GetUserById(userId);
        return await _userManager.ConfirmEmailAsync(user, token);
    }

    public async Task ResetPasswordAsync(Guid userId, string token, string password)
    {
        var user = await GetUserById(userId);

        if (user == null)
            throw new Exception($"User with userId {userId} not found!");
        
        var result = await _userManager.ResetPasswordAsync(user, token, password);

        if (!result.Succeeded)
        {
            throw new Exception($"Error in resetting password");
        }
    }

    public async Task SetDisabledAsync(Guid userId, bool disabled, CancellationToken cancellationToken = default)
    {
        var user = await GetUserById(userId);
        var lockoutEnd = disabled ? DateTimeOffset.MaxValue : (DateTimeOffset?)null;

        var result = await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        if (!result.Succeeded)
        {
            throw new Exception(disabled ? "Failed to disable user." : "Failed to enable user.");
        }

        await RevokeAllRefreshTokensAsync(userId, cancellationToken);

        await _auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = disabled ? AuditActions.Disabled : AuditActions.Enabled,
            EntityType = AuditEntityTypes.User,
            EntityId = userId,
            EventType = disabled ? AuditEventTypes.UserDisabled : AuditEventTypes.UserEnabled,
            Message = disabled
                ? $"User '{user.Email ?? user.UserName ?? userId.ToString()}' was disabled"
                : $"User '{user.Email ?? user.UserName ?? userId.ToString()}' was enabled",
            Outcome = AuditOutcomes.Success,
            ActorUserId = ActorUserId,
            TargetUserId = userId,
            Metadata = new
            {
                user.Email,
                user.UserName,
                LockoutEnd = lockoutEnd
            }
        }, cancellationToken);
    }

    public async Task EnsureCanManageUserLockStateAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == targetUserId)
        {
            throw new InvalidOperationException("You cannot disable or enable your own account.");
        }

        if (await IsInRoleAsync(targetUserId, UserRole.SystemAdministrator, cancellationToken))
        {
            throw new InvalidOperationException("System administrator accounts cannot be disabled or enabled from this admin surface.");
        }
    }

    public async Task EnsureCanHardDeleteUserAsync(
        Guid actorUserId,
        Guid targetUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!actorIsSystemAdministrator)
        {
            throw new InvalidOperationException("Only system administrators can permanently delete users.");
        }

        if (actorUserId == targetUserId)
        {
            throw new InvalidOperationException("You cannot delete your own account.");
        }

        if (await IsInRoleAsync(targetUserId, UserRole.SystemAdministrator, cancellationToken))
        {
            throw new InvalidOperationException("System administrator accounts cannot be permanently deleted.");
        }

        if (await IsInRoleAsync(targetUserId, UserRole.Administrator, cancellationToken))
        {
            throw new InvalidOperationException("Administrator accounts cannot be permanently deleted.");
        }
    }

    public async Task EnsureCanAddRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        string roleName,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!actorIsSystemAdministrator)
        {
            throw new InvalidOperationException("Only system administrators can manage admin roles.");
        }

        if (actorUserId == targetUserId)
        {
            throw new InvalidOperationException("You cannot change your own roles.");
        }

        if (string.Equals(roleName, UserRole.SystemAdministrator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("System administrator role changes are not allowed from this admin surface.");
        }

        if (await IsInRoleAsync(targetUserId, UserRole.SystemAdministrator, cancellationToken))
        {
            throw new InvalidOperationException("System administrator accounts cannot be changed from this admin surface.");
        }
    }

    public async Task EnsureCanRemoveRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        string roleName,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!actorIsSystemAdministrator)
        {
            throw new InvalidOperationException("Only system administrators can manage admin roles.");
        }

        if (actorUserId == targetUserId)
        {
            throw new InvalidOperationException("You cannot change your own roles.");
        }

        if (string.Equals(roleName, UserRole.SystemAdministrator, StringComparison.Ordinal))
        {
            var sysAdminCount = await CountUsersInRoleAsync(UserRole.SystemAdministrator, cancellationToken);
            if (sysAdminCount <= 1)
            {
                throw new InvalidOperationException("You cannot remove the last system administrator.");
            }

            throw new InvalidOperationException("System administrator role changes are not allowed from this admin surface.");
        }

        if (await IsInRoleAsync(targetUserId, UserRole.SystemAdministrator, cancellationToken))
        {
            throw new InvalidOperationException("System administrator accounts cannot be changed from this admin surface.");
        }
    }

    public Task HardDeleteAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _hardDeleteService.DeleteAsync(userId, ActorUserId, cancellationToken);

    public async Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        return await _db.Set<ApplicationUserRole>()
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(
                _db.Set<ApplicationRole>().AsNoTracking(),
                ur => ur.RoleId,
                role => role.Id,
                (ur, role) => role.Name)
            .AnyAsync(name => name == roleName, cancellationToken);
    }

    public async Task<int> CountUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        return await _db.Set<ApplicationUserRole>()
            .AsNoTracking()
            .Join(
                _db.Set<ApplicationRole>().AsNoTracking(),
                ur => ur.RoleId,
                role => role.Id,
                (ur, role) => new { ur.UserId, role.Name })
            .Where(x => x.Name == roleName)
            .Select(x => x.UserId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<string>> GetRoles(Guid userId)
    {
        return await _userManager.GetRolesAsync(await GetUserById(userId));
    }

    public async Task AddRole(Guid userId, string roleName)
    {
        var user = await GetUserById(userId);
        var result = await _userManager.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            throw new Exception("Error in Adding Role!");
        }

        await _auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Added,
            EntityType = AuditEntityTypes.UserRole,
            EntityId = userId,
            EventType = AuditEventTypes.UserRoleChanged,
            Message = $"Role '{roleName}' was added to user '{user.Email ?? user.UserName ?? userId.ToString()}'",
            Outcome = AuditOutcomes.Success,
            ActorUserId = ActorUserId,
            TargetUserId = userId,
            Metadata = new
            {
                RoleName = roleName,
                ChangeType = AuditActions.Added,
                user.Email,
                user.UserName
            }
        });
    }

    public async Task AddRole(Guid userId, Guid roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        var result = await _userManager.AddToRoleAsync(await GetUserById(userId), role.Name);
        if (!result.Succeeded)
        {
            throw new Exception("Error in Adding Role!");
        }
    }

    public async Task RemoveRole(Guid userId, string roleName)
    {
        var user = await GetUserById(userId);
        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            throw new Exception("Error in Removing Role!");
        }

        await _auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            Action = AuditActions.Removed,
            EntityType = AuditEntityTypes.UserRole,
            EntityId = userId,
            EventType = AuditEventTypes.UserRoleChanged,
            Message = $"Role '{roleName}' was removed from user '{user.Email ?? user.UserName ?? userId.ToString()}'",
            Outcome = AuditOutcomes.Success,
            ActorUserId = ActorUserId,
            TargetUserId = userId,
            Metadata = new
            {
                RoleName = roleName,
                ChangeType = AuditActions.Removed,
                user.Email,
                user.UserName
            }
        });
    }

    public async Task RemoveRole(Guid userId, Guid roleId)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        var result = await _userManager.RemoveFromRoleAsync(await GetUserById(userId), role.Name);
        if (!result.Succeeded)
        {
            throw new Exception("Error in Removing Role!");
        }
    }

    /// <summary>
    /// Generate a unique public username from user's display name
    /// </summary>
    public async Task<string> GenerateUniquePublicUsernameAsync(string displayName)
    {
        // Convert display name to URL-safe slug
        var slug = ConvertToSlug(displayName);
        
        // Check if slug is unique
        var publicUsername = slug;
        
        while (await _userManager.Users.AnyAsync(u => u.PublicUsername != null && u.PublicUsername.ToLower() == publicUsername.ToLower()))
        {
            // Append random hex suffix for uniqueness (e.g., "john-a7f2b5")
            var randomSuffix = Guid.NewGuid().ToString("N").Substring(0, 6);
            publicUsername = $"{slug}-{randomSuffix}";
        }
        
        return publicUsername;
    }

    public async Task<string> GenerateUniquePublicUsernameFromCandidateAsync(string candidate)
    {
        var baseSlug = ConvertToSlug(candidate);
        if (baseSlug.Length < 3)
        {
            baseSlug = baseSlug.PadRight(3, '0');
        }

        var publicUsername = baseSlug;

        while (await _userManager.Users.AnyAsync(u => u.PublicUsername != null && u.PublicUsername.ToLower() == publicUsername.ToLower()))
        {
            var randomDigits = Random.Shared.Next(1000, 10000).ToString();
            var maxBaseLength = Math.Max(0, 32 - randomDigits.Length);
            var trimmedBase = baseSlug.Length > maxBaseLength
                ? baseSlug[..maxBaseLength].TrimEnd('-', '_')
                : baseSlug;

            if (trimmedBase.Length < 3)
            {
                trimmedBase = trimmedBase.PadRight(3, '0');
            }

            publicUsername = $"{trimmedBase}{randomDigits}";
        }

        return publicUsername;
    }

    /// <summary>
    /// Check if a public username is available (not already taken)
    /// </summary>
    public async Task<bool> IsPublicUsernameAvailableAsync(string publicUsername)
    {
        if (string.IsNullOrWhiteSpace(publicUsername))
            return false;

        // Validate format
        if (!IsValidPublicUsername(publicUsername))
            return false;

        // Check if already in use
        return !await _userManager.Users.AnyAsync(u => u.PublicUsername != null && u.PublicUsername.ToLower() == publicUsername.ToLower());
    }

    /// <summary>
    /// Validate public username format
    /// </summary>
    public bool IsValidPublicUsername(string publicUsername)
    {
        if (string.IsNullOrWhiteSpace(publicUsername))
            return false;

        if (publicUsername.Length < 3 || publicUsername.Length > 32)
            return false;

        // Must match pattern: alphanumeric, hyphens, underscores only
        return Regex.IsMatch(publicUsername, @"^[a-zA-Z0-9_\-]+$");
    }

    /// <summary>
    /// Convert display name to URL-safe slug
    /// </summary>
    private static string ConvertToSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "user";

        // Convert to lowercase
        var slug = input.ToLowerInvariant();

        // Remove non-ASCII characters (accents, special chars)
        slug = new string(slug.Where(c => (int)c < 128).ToArray());

        // Remove invalid characters (keep only alphanumeric, spaces, hyphens, underscores)
        slug = Regex.Replace(slug, @"[^a-z0-9\s\-_]", "");

        // Replace spaces with hyphens
        slug = Regex.Replace(slug, @"\s+", "-");

        // Remove multiple consecutive hyphens
        slug = Regex.Replace(slug, @"-+", "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-', '_');

        // Ensure it's not empty
        if (string.IsNullOrWhiteSpace(slug))
            slug = "user";

        // Limit to 32 characters for URL safety
        if (slug.Length > 32)
            slug = slug.Substring(0, 32).TrimEnd('-', '_');

        return slug;
    }

    private async Task AssignDefaultPlanAsync(Guid userId)
    {
        try
        {
            var defaultPlan = await _db.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IsDefault && p.IsActive);

            if (defaultPlan != null)
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user != null)
                {
                    user.SubscriptionPlanId = defaultPlan.Id;
                    await _userManager.UpdateAsync(user);
                }
            }
        }
        catch
        {
            // Never block registration because of billing setup failure
        }
    }
}
