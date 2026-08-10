using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RuntimeTunnelRoutingTests
{
    [Test]
    public void RuntimeTunnelRoute_PrecedesPublishedSubdomainCatchAll()
    {
        var route = (RouteAttribute?)typeof(RuntimeTunnelController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Single();
        var publishedCatchAllOrder = -10;

        Assert.That(route.Order, Is.LessThan(publishedCatchAllOrder));
    }
}
