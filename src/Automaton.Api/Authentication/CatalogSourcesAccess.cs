using System.Security.Claims;

namespace Automaton.Api.Authentication;

/// <summary>
/// Catalog sources decide which code an instance can install, so they are managed by a system administrator only.
/// Besides a Studio login, the administrator's own API key may call them from the CLI, but only a key made for
/// development work (the scope `automaton login` creates) or a full-access key; read-only, execute, runner and
/// web-app keys stay refused. The role check on the controller still applies, so a key of anyone else gets 403.
/// </summary>
public static class CatalogSourcesAccess
{
    public const string PolicyName = "CatalogSourcesAdministration";

    public static bool IsAllowed(ClaimsPrincipal user) => AppChangeKeyScope.IsAllowed(user);
}
