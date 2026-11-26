using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Automaton.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class ApiKeyFailureLimiterTests
{
    private static ApiKeyFailureLimiter Limiter(int limit = 3) => new(Options.Create(new RateLimitingOptions
    {
        ApiKeyFailures = new RateLimitPolicyOptions(limit, 60)
    }));

    private static DefaultHttpContext From(string address, string? key = "ak_bad")
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddMemoryCache().BuildServiceProvider() };
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        if (key is not null) context.Request.Headers["X-API-Key"] = key;
        return context;
    }

    private static async Task<int> Send(ApiKeyFailureMiddleware middleware, HttpContext context)
    {
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    [Test]
    public async Task AddressOverTheLimit_Gets429_OtherAddressesDoNot()
    {
        var limiter = Limiter();
        var middleware = new ApiKeyFailureMiddleware(c => { c.Response.StatusCode = 401; return Task.CompletedTask; }, limiter);

        for (var i = 0; i < 3; i++)
        {
            Assert.That(await Send(middleware, From("203.0.113.5")), Is.EqualTo(401));
            limiter.RecordFailure(From("203.0.113.5"));
        }

        var blocked = From("203.0.113.5");
        Assert.That(await Send(middleware, blocked), Is.EqualTo(429));
        Assert.That(blocked.Response.Headers.RetryAfter.ToString(), Is.Not.Empty);
        Assert.That(await Send(middleware, From("203.0.113.6")), Is.EqualTo(401));
    }

    [Test]
    public async Task RequestsWithoutAnApiKey_AreNeverBlockedByTheFailureLimit()
    {
        var limiter = Limiter(1);
        limiter.RecordFailure(From("203.0.113.5"));
        var middleware = new ApiKeyFailureMiddleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; }, limiter);

        Assert.That(await Send(middleware, From("203.0.113.5", key: null)), Is.EqualTo(200));
    }

    [Test]
    public void BlockEndsWhenTheWindowEnds()
    {
        var clock = new ManualClock();
        var limiter = new ApiKeyFailureLimiter(Options.Create(new RateLimitingOptions { ApiKeyFailures = new(1, 60) })) { Clock = clock };
        limiter.RecordFailure(From("203.0.113.5"));
        Assert.That(limiter.IsBlocked(From("203.0.113.5")), Is.True);

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.That(limiter.IsBlocked(From("203.0.113.5")), Is.False);
    }

    [Test]
    public void Defaults_LimitForgotPasswordToFiveAndCoverInvites()
    {
        var options = new RateLimitingOptions();
        Assert.That(options.AuthForgotPassword.PermitLimit, Is.EqualTo(5));
        Assert.That(options.AuthInviteAccept.PermitLimit, Is.GreaterThan(0));
        Assert.That(new RateLimitingOptionsValidation().Validate(null, options).Succeeded, Is.True);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }
}
