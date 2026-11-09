using Automaton.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ApiNotFoundTests
{
    private IHost host = null!;
    private HttpClient client = null!;

    [SetUp]
    public async Task SetUp()
    {
        host = await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services => services.AddRouting())
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api/known", () => "known");
                    endpoints.MapApiNotFound();
                    // Stands in for the Studio page fallback.
                    endpoints.MapFallback("{*path:nonfile}", () => Microsoft.AspNetCore.Http.Results.Content("<html>studio</html>", "text/html"));
                });
            })).StartAsync();
        client = host.GetTestClient();
    }

    [TearDown]
    public void TearDown()
    {
        client.Dispose();
        host.Dispose();
    }

    [TestCase("GET")]
    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    [TestCase("PATCH")]
    public async Task UnmatchedApiPath_AnswersJson404ForEveryMethod(string method)
    {
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/api/does/not/exist"));

        Assert.That((int)response.StatusCode, Is.EqualTo(404));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Does.Contain("json"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("<html"));
    }

    [Test]
    public async Task KnownApiPath_IsUnchanged() =>
        Assert.That(await client.GetStringAsync("/api/known"), Is.EqualTo("known"));

    [Test]
    public async Task NonApiPath_KeepsStudioFallback()
    {
        var response = await client.GetAsync("/apps/123");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("studio"));
    }
}
