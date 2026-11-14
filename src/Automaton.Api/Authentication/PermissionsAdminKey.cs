using System.Security.Claims;
using System.Text.RegularExpressions;
using Automaton.Api.Enums;
using Automaton.Core.Enums;

namespace Automaton.Api.Authentication;

/// <summary>
/// The opt-in admin API key: lets a system administrator's CLI approve or reject app permissions and nothing else.
/// Three layers keep it narrow: it can be created only by an administrator signed in to Studio, the API-key handler
/// accepts it only on the paths below, and the policy refuses every other admin-role key scope.
/// </summary>
public static partial class PermissionsAdminKey
{
    public const string PolicyName = "AppPermissionsAdministration";
    public const int MaximumLifetimeDays = 90;
    public const int DefaultLifetimeDays = 30;
    /// <summary>A CLI login request for this key is open for less time than a normal login (10 minutes).</summary>
    public const int LoginRequestLifetimeMinutes = 5;

    /// <summary>Only these paths authenticate a key of this scope; every other request gets 401.</summary>
    [GeneratedRegex(@"^/api/admin/(apps/[0-9a-fA-F-]{36}/(permissions(/[^/]+)?|runner-permissions(/[0-9a-fA-F-]{36})?)|app-permissions/pending)/?\z")]
    private static partial Regex AllowedPath();

    public static bool IsAllowedPath(string path) => AllowedPath().IsMatch(path);

    /// <summary>A login (no key) or this key scope; the role is checked by the policy and re-read from the database on every call.</summary>
    public static bool IsAllowed(ClaimsPrincipal user)
    {
        var scope = user.FindFirst("api_key_scope")?.Value;
        return string.IsNullOrWhiteSpace(scope) || scope == nameof(ApiKeyScope.PermissionsAdmin);
    }

    public static bool IsAdministrator(IEnumerable<string> roles) => roles.Contains(UserRole.SystemAdministrator);
}
