using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Automaton.Studio.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AppsHostOnlyAttribute : ActionMethodSelectorAttribute
{
    public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
    {
        var host = routeContext.HttpContext.Request.Host.Host;
        return host.StartsWith("apps.", StringComparison.OrdinalIgnoreCase);
    }
}
