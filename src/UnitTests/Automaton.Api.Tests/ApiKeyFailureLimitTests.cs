using Automaton.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;

namespace Automaton.Api.Tests;

[TestFixture]
public class ApiKeyFailureLimitTests
{
    private static async Task<IHost> CreateHostAsync(int limit, Action<ApiKeyFailureTracker>? seed = null)
    {
        var host = await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddMemoryCache();
                services.AddSingleton(Options.Create(new RateLimitingOptions { ApiKeyFailures = new RateLimitPolicyOptions(limit, 60) }));
                services.AddSingleton<ApiKeyFailureTracker>();
            })
            .Configure(app =>
            {
                app.UseMiddleware<ApiKeyFailureMiddleware>();
                app.Run(context => { context.Response.StatusCode = 200; return Task.CompletedTask; });
            })).StartAsync();
        seed?.Invoke(host.Services.GetRequiredService<ApiKeyFailureTracker>());
        return host;
    }

    [Test]
    public async Task AddressOverTheFailureLimit_GetsTooManyRequestsOnlyWhenItSendsAnApiKey()
    {
        using var host = await CreateHostAsync(3, tracker => { for (var i = 0; i < 3; i++) tracker.RecordFailure(null); });
        var client = host.GetTestClient();

        var withKey = new HttpRequestMessage(HttpMethod.Get, "/api/apps");
        withKey.Headers.Add("X-API-Key", "ak_wrong");
        var blocked = await client.SendAsync(withKey);
        var withoutKey = await client.GetAsync("/api/apps");

        Assert.That(blocked.StatusCode, Is.EqualTo((HttpStatusCode)429));
        Assert.That(blocked.Headers.GetValues(ApiKeyFailureTracker.HeaderName), Is.EqualTo(new[] { ApiKeyFailureTracker.HeaderValue }));
        Assert.That(withoutKey.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AddressUnderTheLimit_IsNotBlocked_AndOtherAddressesAreIndependent()
    {
        using var host = await CreateHostAsync(3, tracker => { for (var i = 0; i < 2; i++) tracker.RecordFailure(null); });
        var tracker = host.Services.GetRequiredService<ApiKeyFailureTracker>();

        Assert.That(tracker.IsBlocked(null), Is.False);
        tracker.RecordFailure(null);
        Assert.That(tracker.IsBlocked(null), Is.True);
        Assert.That(tracker.IsBlocked("203.0.113.9"), Is.False);
    }
}
