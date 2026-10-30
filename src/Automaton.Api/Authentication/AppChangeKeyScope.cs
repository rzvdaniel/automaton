using System.Security.Claims;
using Automaton.Core.Enums;

namespace Automaton.Api.Authentication;

/// <summary>
/// API keys may change apps only with a Developer or FullAccess scope.
/// Identity and JWT callers carry no API-key scope claim and are allowed.
/// </summary>
public static class AppChangeKeyScope
{
    public static bool IsAllowed(ClaimsPrincipal user)
    {
        var scope = user.FindFirst("api_key_scope")?.Value;
        if (string.IsNullOrWhiteSpace(scope))
        {
            return true;
        }

        return Enum.TryParse<ApiKeyScope>(scope, out var apiKeyScope)
            && apiKeyScope is ApiKeyScope.Developer or ApiKeyScope.FullAccess;
    }
}
