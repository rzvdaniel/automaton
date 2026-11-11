using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppFramingProbeTests
{
    private const string Studio = "https://studio.example.com";

    [Test]
    public async Task Allows_framing_when_the_app_has_no_default_target()
    {
        await using var dbContext = NewDbContext();
        var forwarder = new FakeForwarder((_, _) => throw new InvalidOperationException("must not be called"));

        Assert.That(await Probe(dbContext, forwarder).CanBeFramedAsync(Guid.NewGuid(), Studio, CancellationToken.None), Is.True);
        Assert.That(forwarder.Paths, Is.Empty);
    }

    [Test]
    public async Task Reports_an_app_that_forbids_framing()
    {
        await using var dbContext = NewDbContext();
        var appId = await SeedTargetAsync(dbContext);
        var forwarder = new FakeForwarder((_, _) => Response(200, ("X-Frame-Options", "DENY")));

        Assert.That(await Probe(dbContext, forwarder).CanBeFramedAsync(appId, Studio, CancellationToken.None), Is.False);
    }

    [Test]
    public async Task Follows_a_same_host_redirect_and_reads_the_final_headers()
    {
        await using var dbContext = NewDbContext();
        var appId = await SeedTargetAsync(dbContext);
        var forwarder = new FakeForwarder((_, path) => path == string.Empty
            ? Response(302, ("Location", "/login"))
            : Response(200, ("Content-Security-Policy", "frame-ancestors 'none'")));

        Assert.That(await Probe(dbContext, forwarder).CanBeFramedAsync(appId, Studio, CancellationToken.None), Is.False);
        Assert.That(forwarder.Paths, Is.EqualTo(new[] { string.Empty, "login" }));
    }

    [Test]
    public async Task Allows_framing_after_more_than_three_redirects()
    {
        await using var dbContext = NewDbContext();
        var appId = await SeedTargetAsync(dbContext);
        var forwarder = new FakeForwarder((_, path) => Response(302, ("Location", "/next"), ("X-Frame-Options", "DENY")));

        Assert.That(await Probe(dbContext, forwarder).CanBeFramedAsync(appId, Studio, CancellationToken.None), Is.True);
        Assert.That(forwarder.Paths, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task Allows_framing_when_the_tunnel_throws()
    {
        await using var dbContext = NewDbContext();
        var appId = await SeedTargetAsync(dbContext);
        var forwarder = new FakeForwarder((_, _) => throw new InvalidOperationException("tunnel down"));

        Assert.That(await Probe(dbContext, forwarder).CanBeFramedAsync(appId, Studio, CancellationToken.None), Is.True);
    }

    [Test]
    public async Task Allows_framing_when_the_whole_check_runs_out_of_time()
    {
        await using var dbContext = NewDbContext();
        var appId = await SeedTargetAsync(dbContext);
        var forwarder = new FakeForwarder((_, _) => Response(200, ("X-Frame-Options", "DENY")))
        {
            Hang = true
        };
        var probe = new AppFramingProbe(dbContext, forwarder, Options(), NullLogger<AppFramingProbe>.Instance)
        {
            Budget = TimeSpan.FromMilliseconds(100)
        };

        Assert.That(await probe.CanBeFramedAsync(appId, Studio, CancellationToken.None), Is.True);
    }

    internal static AppFramingProbe Probe(ApplicationDbContext dbContext, ITunnelForwarder forwarder)
        => new(dbContext, forwarder, Options(), NullLogger<AppFramingProbe>.Instance);

    internal static IOptions<PublishedAppsConfig> Options()
        => Microsoft.Extensions.Options.Options.Create(new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.test" });

    internal static ApplicationDbContext NewDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    internal static async Task<Guid> SeedTargetAsync(ApplicationDbContext dbContext, Guid? appId = null)
    {
        var id = appId ?? Guid.NewGuid();
        dbContext.AppDeploymentTargets.Add(AppDeploymentTarget.CreateDefault(id));
        await dbContext.SaveChangesAsync();
        return id;
    }

    internal static TunnelResponse Response(int status, params (string Name, string Value)[] headers) => new()
    {
        StatusCode = status,
        HeaderValues = headers.ToDictionary(h => h.Name, h => new[] { h.Value }, StringComparer.OrdinalIgnoreCase)
    };

    internal sealed class FakeForwarder(Func<Guid, string, TunnelResponse> respond) : ITunnelForwarder
    {
        public List<string> Paths { get; } = [];
        public bool Hang { get; init; }

        public async Task<TunnelResponse> ForwardAsync(
            Guid appId, Guid deploymentTargetId, HttpRequest request, string path, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return respond(appId, path);
        }
    }
}
