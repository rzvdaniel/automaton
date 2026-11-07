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
            .Single() ?? throw new InvalidOperationException("The runtime tunnel controller must define a route.");
        var publishedCatchAllOrder = -10;

        Assert.That(route.Order, Is.LessThan(publishedCatchAllOrder));
    }

    [Test]
    public void RuntimeTunnelAction_HasNoParameters_SoMvcNeverReadsAFormBody()
        => Assert.That(typeof(RuntimeTunnelController).GetMethod(nameof(RuntimeTunnelController.ProxyAsync))!.GetParameters(),
            Is.Empty);

    [TestCase("application/x-www-form-urlencoded", "a=1&b=two")]
    [TestCase("application/json", "{\"a\":1}")]
    [TestCase(null, "raw bytes")]
    public async Task ParameterlessAction_LeavesTheRequestBodyUnread(string? contentType, string payload)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(BodyProbeController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe/x/deep/path")
        {
            Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(payload))
        };
        request.Content.Headers.Remove("Content-Type");
        if (contentType is not null) request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        var response = await app.GetTestClient().SendAsync(request);

        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(payload));
        await app.DisposeAsync();
    }

    [Test]
    public async Task ActionWithParameters_ConsumesAFormBody()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(BodyProbeController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/probe-with-parameters/x",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("a", "1")]));

        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty,
            "documents why the tunnel action takes no parameters");
        await app.DisposeAsync();
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

[Route("probe/{id}/{**path}")]
public sealed class BodyProbeController : ControllerBase
{
    [HttpPost]
    public async Task Echo() => await Request.Body.CopyToAsync(Response.Body);
}

[Route("probe-with-parameters/{id}")]
public sealed class BodyProbeWithParametersController : ControllerBase
{
    [HttpPost]
    public async Task Echo(string id) => await Request.Body.CopyToAsync(Response.Body);
}
