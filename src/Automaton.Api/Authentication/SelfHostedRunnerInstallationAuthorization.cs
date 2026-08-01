using System.Security.Claims;
using Automaton.Core.Enums;

namespace Automaton.Api.Authentication;

public static class SelfHostedRunnerInstallationAuthorization
{
    public const string PolicyName = "SelfHostedRunnerInstallation";

    public static bool IsAllowed(ClaimsPrincipal user)
    {
        var scope = user.FindFirst("api_key_scope")?.Value;
        if (string.IsNullOrWhiteSpace(scope))
        {
            // Identity and JWT callers do not have an API-key scope claim.
            return true;
        }

        return Enum.TryParse<ApiKeyScope>(scope, out var apiKeyScope)
            && apiKeyScope == ApiKeyScope.Developer;
    }
}
