using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Automaton.Api.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PublishedAppSubdomainOnlyAttribute : ActionMethodSelectorAttribute
{
    public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
    {
        var host = routeContext.HttpContext.Request.Host.Host;
        var markerIndex = host.IndexOf(".apps.", StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            return false;
        }

        var subdomain = host[..markerIndex];
        return !subdomain.StartsWith("app-", StringComparison.OrdinalIgnoreCase)
            || subdomain.Length != 36
            || !subdomain[4..].All(Uri.IsHexDigit);
    }
}
