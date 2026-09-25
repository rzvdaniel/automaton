using System.Net;
using Automaton.Api.Pages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class LogoutAntiforgeryTests
{
    [Test]
    public async Task LogoutPost_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        using var host = await CreateHostAsync();

        using var response = await host.GetTestClient().PostAsync(
            "/Identity/Account/Logout", new StringContent(string.Empty));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static Task<IHost> CreateHostAsync()
        => new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                    services.AddRazorPages().AddApplicationPart(typeof(ErrorModel).Assembly));
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapRazorPages());
                });
            })
            .StartAsync();
}
