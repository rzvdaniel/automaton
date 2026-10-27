using Automaton.Api.Controllers;
using Automaton.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

    [Test]
    public async Task CorsMiddleware_ForwardsAppPreflight()
    {
        await using var app = await CreateHostAsync();
        using var request = Preflight("/runtime-tunnel/apps/1/targets/2/tasks", "https://web.example.test");
        var response = await app.GetTestClient().SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK));
            Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single(),
                Is.EqualTo("https://web.example.test"));
        });
    }

    [Test]
    public async Task CorsMiddleware_KeepsControlApiPolicy()
    {
        await using var app = await CreateHostAsync();
        using var request = Preflight("/api/test", "https://studio.example.test");
        var response = await app.GetTestClient().SendAsync(request);

        Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single(),
            Is.EqualTo("https://studio.example.test"));
    }

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return request;
    }

    private static async Task<WebApplication> CreateHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddCors(options => options.AddPolicy("CorsPolicy", policy =>
            policy.WithOrigins("https://studio.example.test").AllowAnyMethod().AllowAnyHeader()));

        var app = builder.Build();
        app.UseRouting();
        ApiCorsMiddleware.Configure(app, ["https://studio.example.test"]);
        app.MapMethods("/runtime-tunnel/apps/{appId}/targets/{targetId}/{**path}", ["OPTIONS"],
            (HttpContext context) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "https://web.example.test";
                return Results.Ok();
            });
        app.MapMethods("/api/test", ["OPTIONS"], () => Results.Ok());
        await app.StartAsync();
        return app;
    }
}
