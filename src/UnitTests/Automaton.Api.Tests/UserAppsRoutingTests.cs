using System.Net;
using Microsoft.AspNetCore.Builder;
using Automaton.Api.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Automaton.Api.Tests;

[TestFixture]
public class UserAppsRoutingTests
{
    [Test]
    public async Task StudioHostedMediaRoute_ResolvesToMediaAction()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/apps/my-blog/media/uploads/test.jpg");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(nameof(UserAppsController.ServeStudioHostedAppMedia)));
    }

    [Test]
    public async Task DedicatedAppsHostMediaRoute_ResolvesToMediaAction()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Host = "apps.example.test";

        var response = await client.GetAsync("/my-blog/media/uploads/test.jpg");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(nameof(UserAppsController.ServeDedicatedAppsHostAppMedia)));
    }

    private static async Task<IHost> CreateHostAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(UserAppsController).Assembly);
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.Use(async (context, next) =>
                    {
                        var action = context.GetEndpoint()?
                            .Metadata
                            .GetMetadata<ControllerActionDescriptor>();

                        if (action != null)
                        {
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await context.Response.WriteAsync(action.ActionName);
                            return;
                        }

                        await next();
                    });
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync();
    }
}
