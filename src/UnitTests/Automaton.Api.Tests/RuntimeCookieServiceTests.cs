using Automaton.Api.Services;
using Automaton.Api.Config;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

public class RuntimeCookieServiceTests
{
    [Test]
    public void ValidateRuntimeCookie_WhenCookieMatchesApp_ReturnsUserAndApp()
    {
        var service = CreateService();
        var appId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var cookie = service.CreateRuntimeCookie(appId, userId, DateTimeOffset.UtcNow.AddMinutes(5));
        var result = service.ValidateRuntimeCookie(cookie, appId);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.AppId, Is.EqualTo(appId));
        Assert.That(result.UserId, Is.EqualTo(userId));
    }

    [Test]
    public void ValidateRuntimeCookie_WhenCookieIsForDifferentApp_ReturnsInvalid()
    {
        var service = CreateService();
        var cookie = service.CreateRuntimeCookie(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));

        var result = service.ValidateRuntimeCookie(cookie, Guid.NewGuid());

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateRuntimeCookie_WhenCookieIsExpired_ReturnsInvalid()
    {
        var service = CreateService();
        var appId = Guid.NewGuid();
        var cookie = service.CreateRuntimeCookie(appId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = service.ValidateRuntimeCookie(cookie, appId);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void BuildRuntimeCookieOptions_WhenApiAndRuntimeHostsAreSiblings_ScopesCookieToSharedParentDomain()
    {
        var service = CreateService(
            apiBaseUrl: "https://api.automaton.monster",
            publicBaseUrl: "https://apps.automaton.monster");
        var appId = Guid.NewGuid();
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5);
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("api.automaton.monster");
        context.Request.Scheme = "https";

        var options = service.BuildRuntimeCookieOptions(appId, context.Request, expiresAtUtc);

        Assert.That(options.HttpOnly, Is.True);
        Assert.That(options.Secure, Is.True);
        Assert.That(options.SameSite, Is.EqualTo(SameSiteMode.Lax));
        Assert.That(options.Path, Is.EqualTo($"/a/{appId}"));
        Assert.That(options.Domain, Is.EqualTo(".automaton.monster"));
        Assert.That(options.Expires, Is.EqualTo(expiresAtUtc));
        Assert.That(options.MaxAge, Is.Not.Null);
        Assert.That(options.MaxAge!.Value, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(options.MaxAge!.Value, Is.GreaterThan(TimeSpan.FromMinutes(4)));
    }

    [Test]
    public void BuildRuntimeCookieOptions_WhenApiAndRuntimeHostsAreNotSiblings_DoesNotSetCookieDomain()
    {
        var service = CreateService(
            apiBaseUrl: "https://api.automaton.monster",
            publicBaseUrl: "https://apps.example.net");
        var appId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        var options = service.BuildRuntimeCookieOptions(appId, context.Request, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.That(options.Domain, Is.Null);
    }

    [Test]
    public void BuildRuntimeCookieOptions_WhenSiblingParentLooksLikePublicSuffix_DoesNotSetCookieDomain()
    {
        var service = CreateService(
            apiBaseUrl: "https://api.example.co.uk",
            publicBaseUrl: "https://apps.example.co.uk");
        var appId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        var options = service.BuildRuntimeCookieOptions(appId, context.Request, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.That(options.Domain, Is.Null);
    }

    [Test]
    public void BuildRuntimeCookieOptions_WhenHostsAreLocalhost_DoesNotSetCookieDomain()
    {
        var service = CreateService();
        var appId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("localhost", 7091);
        context.Request.Scheme = "https";

        var options = service.BuildRuntimeCookieOptions(appId, context.Request, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.That(options.Secure, Is.True);
        Assert.That(options.Domain, Is.Null);
    }

    private static RuntimeCookieService CreateService(
        string publicBaseUrl = "http://localhost:5173",
        string apiBaseUrl = "https://localhost:7091")
    {
        var keyDirectory = Directory.CreateTempSubdirectory("automaton-runtime-cookie-test-");
        var provider = DataProtectionProvider.Create(keyDirectory);
        var options = Options.Create(new PublishedAppsConfig
        {
            PublicBaseUrl = publicBaseUrl,
            ApiBaseUrl = apiBaseUrl
        });

        return new RuntimeCookieService(provider, options);
    }
}
