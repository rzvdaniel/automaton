using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Tests;

[TestFixture]
public class RuntimeTunnelPolicyTests
{
    [TestCase("Managed", "Standard", true)]
    [TestCase("Managed", "PrivilegedHost", true)]
    [TestCase("Infrastructure", null, false)]
    [TestCase("Managed", "Unknown", false)]
    public void AllowsRuntimeTunnel_OnlyAllowsApplicationProfiles(string purpose, string? profile, bool expected)
    {
        Assert.That(SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(purpose, profile), Is.EqualTo(expected));
    }

    [Test]
    public async Task ReadBodyAsync_ReadsUnknownLengthBodyWithinLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("chunked-body"));
        context.Request.ContentLength = null;

        var result = await RuntimeTunnelService.ReadBodyAsync(context.Request, 64, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TooLarge, Is.False);
            Assert.That(Encoding.UTF8.GetString(result.Body!), Is.EqualTo("chunked-body"));
        });
    }

    [Test]
    public async Task ReadBodyAsync_ReadsKnownLengthBodyWithinLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("known-body"));
        context.Request.ContentLength = 10;

        var result = await RuntimeTunnelService.ReadBodyAsync(context.Request, 64, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TooLarge, Is.False);
            Assert.That(Encoding.UTF8.GetString(result.Body!), Is.EqualTo("known-body"));
        });
    }

    [Test]
    public async Task ReadBodyAsync_RejectsUnknownLengthBodyAboveLimit()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("too-large"));
        context.Request.ContentLength = null;

        var result = await RuntimeTunnelService.ReadBodyAsync(context.Request, 3, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.TooLarge, Is.True);
            Assert.That(result.Body, Is.Null);
        });
    }

    [Test]
    public void IsExpectedAppHost_RejectsDirectApiHost()
    {
        var appId = Guid.NewGuid();
        var target = AppDeploymentTarget.CreateDefault(appId);
        target.WebApp = new WebApp
        {
            Id = appId,
            Name = "Private app",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            RuntimeVisibility = AppRuntimeVisibilities.Private
        };
        var config = new PublishedAppsConfig { PublicBaseUrl = "https://apps.automaton.monster" };

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeTunnelService.IsExpectedAppHost(
                $"app-{appId:N}.apps.automaton.monster", target, config), Is.True);
            Assert.That(RuntimeTunnelService.IsExpectedAppHost("api.automaton.monster", target, config), Is.False);
        });
    }

    [Test]
    public void RunnerPresenceEvaluator_RejectsOfflineRunner()
    {
        Assert.That(RunnerPresenceEvaluator.IsOnline(
            "stale-connection",
            DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold - TimeSpan.FromSeconds(1),
            DateTime.UtcNow), Is.False);
    }
}
