using System.Security.Claims;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for safely extracting and working with claims from authenticated users.
/// Handles different claim types from various authentication schemes (JWT, Identity, API Key).
/// </summary>
public interface IClaimUtilityService
{
    /// <summary>
    /// Gets the user's email from claims, checking multiple common claim types.
    /// 
    /// Checks in order:
    /// 1. Standard "email" claim
    /// 2. JWT "unique_name" claim (often contains email)
    /// 3. ClaimTypes.Email
    /// 4. ClaimTypes.NameIdentifier (fallback)
    /// </summary>
    /// <param name="user">The ClaimsPrincipal representing the authenticated user</param>
    /// <returns>The user's email or identity value, or null if not found</returns>
    string? GetUserEmail(ClaimsPrincipal user);

    /// <summary>
    /// Gets the user's ID from claims, checking multiple common claim types.
    /// 
    /// Checks in order:
    /// 1. Custom "uid" claim
    /// 2. ClaimTypes.NameIdentifier
    /// 3. "sub" claim (JWT subject)
    /// </summary>
    /// <param name="user">The ClaimsPrincipal representing the authenticated user</param>
    /// <returns>The user's ID or null if not found</returns>
    string? GetUserId(ClaimsPrincipal user);

    /// <summary>
    /// Gets the user's display name from claims.
    /// 
    /// Checks in order:
    /// 1. ClaimTypes.Name
    /// 2. "name" claim
    /// 3. Email (as fallback)
    /// </summary>
    /// <param name="user">The ClaimsPrincipal representing the authenticated user</param>
    /// <returns>The user's display name or null if not found</returns>
    string? GetUserName(ClaimsPrincipal user);

    /// <summary>
    /// Compares two email addresses for equality, ignoring case.
    /// Safely handles null values.
    /// </summary>
    /// <param name="email1">First email to compare</param>
    /// <param name="email2">Second email to compare</param>
    /// <returns>True if emails match (case-insensitive), false otherwise</returns>
    bool EmailsMatch(string? email1, string? email2);
}
