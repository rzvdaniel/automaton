using System.Net;
using System.Net.Http.Json;
using Automaton.Api.Models;
using Automaton.Api.Extensions;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppListMvcSerializationTests
{
    [Test]
    public async Task GetApps_SerializesRunnerConnectivityAsString()
    {
        using var host = await new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services => services.AddControllers()
                .AddApplicationPart(typeof(AppListSerializationController).Assembly)
                .AddJsonOptions(AutomatonJsonOptions.Configure));
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        }).StartAsync();

        var response = await host.GetTestClient().GetAsync("/api/apps");
        var json = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(json, Does.Contain("\"connectivityStatus\":\"Online\""));
    }
}

[ApiController]
[Route("api/apps")]
public sealed class AppListSerializationController : ControllerBase
{
    [HttpGet]
    public AppListItemDto Get() => new()
    {
        Id = Guid.NewGuid(), Name = "app", UpdatedAtUtc = DateTime.UtcNow,
        Runner = new AppListRunnerDto { Id = Guid.NewGuid(), Name = "runner", Purpose = "managed", ConnectivityStatus = RunnerStatus.Online }
    };
}
