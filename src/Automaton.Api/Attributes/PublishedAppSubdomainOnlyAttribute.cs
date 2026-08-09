using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Automaton.Api.Config;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PublishedAppSubdomainOnlyAttribute : ActionMethodSelectorAttribute
{
    public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
    {
        var host = routeContext.HttpContext.Request.Host.Host;
        var config = routeContext.HttpContext.RequestServices
            .GetRequiredService<IOptions<PublishedAppsConfig>>().Value;
        return PublishedAppHostNames.TryGetPublishedAppSlug(
            host, config.PublicBaseUrlHost, out _);
    }
}
