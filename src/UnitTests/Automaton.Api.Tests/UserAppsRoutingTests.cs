using System.Net;
using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Automaton.Api.Tests;

[TestFixture]
public class UserAppsRoutingTests
{
    [Test]
    public async Task RestrictedAppRoute_ResolvesToRestrictedAppAction()
    {
        using var host = await ControllerRouteProbeHost.StartAsync(typeof(UserAppsController).Assembly);
        var client = host.GetTestClient();

        var response = await client.GetAsync($"/a/{Guid.NewGuid()}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(nameof(UserAppsController.ServeRestrictedApp)));
    }

    [Test]
    public async Task RestrictedAppFileRoute_ResolvesToRestrictedAppFileAction()
    {
        using var host = await ControllerRouteProbeHost.StartAsync(typeof(UserAppsController).Assembly);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Host = "apps.example.test";

        var response = await client.GetAsync($"/a/{Guid.NewGuid()}/uploads/test.jpg");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(nameof(UserAppsController.ServeRestrictedAppFile)));
    }
}
