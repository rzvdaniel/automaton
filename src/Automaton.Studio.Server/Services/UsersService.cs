using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Automaton.Studio.Server.Services;

public class UsersService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ApplicationDbContext _db;

    public UsersService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext db)
    {
        _userManager         = userManager;
        _roleManager         = roleManager;
        _db                  = db;
    }

    public async Task CreateUser(ApplicationUser user, string password)
    {
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

    public async Task<bool> ValidateCredentialsAsync(string usernameOrEmail, string password)
    {
        var user = await _userManager.FindByEmailAsync(usernameOrEmail) ??
                   await _userManager.FindByNameAsync(usernameOrEmail);

        if (user != null)
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

    public async Task<IEnumerable<ApplicationUser>> GetUsers(string name, string email,
        int pageindex, int pageSize)
    {
        await Task.CompletedTask;
        var result = _userManager.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Where( u =>
                (name == null || name == "" || u.Name.StartsWith(name)) &&
                (email == null || email == "" || u.Name == name)
            )
            .Skip(pageindex * pageSize)
            .Take(pageSize);

        return result;
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

    public async Task<IEnumerable<string>> GetRoles(Guid userId)
    {
        return await _userManager.GetRolesAsync(await GetUserById(userId));
    }

    public async Task AddRole(Guid userId, string roleName)
    {
        var result = await _userManager.AddToRoleAsync(await GetUserById(userId), roleName);
        if (!result.Succeeded)
        {
            throw new Exception("Error in Adding Role!");
        }
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
        var result = await _userManager.RemoveFromRoleAsync(await GetUserById(userId), roleName);
        if (!result.Succeeded)
        {
            throw new Exception("Error in Removing Role!");
        }
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
    private string ConvertToSlug(string input)
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
