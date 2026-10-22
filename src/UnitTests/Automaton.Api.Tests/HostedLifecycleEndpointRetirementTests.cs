using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Api.Tests;

public sealed class HostedLifecycleEndpointRetirementTests
{
    [TestCase("POST", "api/runners/{id}/restart-hosted")]
    [TestCase("POST", "api/admin/hosted-runners/runners/{id:guid}/restart")]
    [TestCase("DELETE", "api/admin/hosted-runners/runners/{id:guid}")]
    [TestCase("POST", "api/infrastructure-commands/{commandId:guid}/secrets")]
    [TestCase("GET", "api/host-container-executions/{operationId:guid}/disposition")]
    [TestCase("POST", "api/host-container-executions/{operationId:guid}/result")]
    public void RetiredMutationEndpointsAreNotRegistered(string verb, string route)
        => Assert.That(RegisteredRoutes().Contains(verb + " " + route.ToLowerInvariant()), Is.False);

    [TestCase("POST", "api/infrastructure-commands/{commandId:guid}/result")]
    [TestCase("GET", "api/host-container-executions/{operationId:guid}/result")]
    public void ValidationAndMachineResultEndpointsRemainRegistered(string verb, string route)
        => Assert.That(RegisteredRoutes().Contains(verb + " " + route.ToLowerInvariant()), Is.True);

    private static HashSet<string> RegisteredRoutes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(RunnersController).Assembly);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(action => action.AttributeRouteInfo?.Template != null)
            .SelectMany(action => action.ActionConstraints!.OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .Select(verb => verb + " " + action.AttributeRouteInfo!.Template!.ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);
    }
}
