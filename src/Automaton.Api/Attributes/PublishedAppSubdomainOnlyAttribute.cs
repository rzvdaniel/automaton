using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Automaton.Api.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PublishedAppSubdomainOnlyAttribute : ActionMethodSelectorAttribute
{
    public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
    {
        var host = routeContext.HttpContext.Request.Host.Host;
        return host.IndexOf(".apps.", StringComparison.OrdinalIgnoreCase) > 0;
    }
}
