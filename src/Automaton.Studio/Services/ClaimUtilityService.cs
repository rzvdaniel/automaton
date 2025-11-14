using System.Security.Claims;

namespace Automaton.Studio.Services;

public class ClaimUtilityService : IClaimUtilityService
{
    public string? GetUserEmail(ClaimsPrincipal user)
    {
        if (user == null)
            return null;

        // Try standard "email" claim first
        var emailClaim = user.Claims.FirstOrDefault(c => c.Type == "email");
        if (!string.IsNullOrEmpty(emailClaim?.Value))
            return emailClaim.Value;

        // Try JWT "unique_name" claim (common in JWT tokens)
        var uniqueNameClaim = user.Claims.FirstOrDefault(c => c.Type == "unique_name");
        if (!string.IsNullOrEmpty(uniqueNameClaim?.Value))
            return uniqueNameClaim.Value;

        // Try ClaimTypes.Email
        var claimTypesEmail = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email);
        if (!string.IsNullOrEmpty(claimTypesEmail?.Value))
            return claimTypesEmail.Value;

        // Fallback to NameIdentifier
        var nameIdentifier = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(nameIdentifier?.Value))
            return nameIdentifier.Value;

        // Last resort: use Identity.Name
        return user.Identity?.Name;
    }

    public string? GetUserId(ClaimsPrincipal user)
    {
        if (user == null)
            return null;

        // Try custom "uid" claim first
        var uidClaim = user.Claims.FirstOrDefault(c => c.Type == "uid");
        if (!string.IsNullOrEmpty(uidClaim?.Value))
            return uidClaim.Value;

        // Try ClaimTypes.NameIdentifier
        var nameId = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(nameId?.Value))
            return nameId.Value;

        // Try JWT "sub" (subject) claim
        var subClaim = user.Claims.FirstOrDefault(c => c.Type == "sub");
        if (!string.IsNullOrEmpty(subClaim?.Value))
            return subClaim.Value;

        return null;
    }

    public string? GetUserName(ClaimsPrincipal user)
    {
        if (user == null)
            return null;

        // Try ClaimTypes.Name
        var nameClaim = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
        if (!string.IsNullOrEmpty(nameClaim?.Value))
            return nameClaim.Value;

        // Try "name" claim
        var customName = user.Claims.FirstOrDefault(c => c.Type == "name");
        if (!string.IsNullOrEmpty(customName?.Value))
            return customName.Value;

        // Fallback to email
        return GetUserEmail(user);
    }

    public bool EmailsMatch(string? email1, string? email2)
    {
        if (string.IsNullOrEmpty(email1) || string.IsNullOrEmpty(email2))
            return false;

        return string.Equals(email1, email2, StringComparison.OrdinalIgnoreCase);
    }
}
