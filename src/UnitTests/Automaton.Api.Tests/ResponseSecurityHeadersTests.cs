using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ResponseSecurityHeadersTests
{
    private static readonly string[] Studio = ["https://studio.example.com"];

    [Test]
    public void JsonResponse_GetsLockedDownCspAndDeny()
    {
        var response = new DefaultHttpContext().Response;
        response.ContentType = "application/json";
        ResponseSecurityHeaders.Apply(response, Studio);
        Assert.That(response.Headers.ContentSecurityPolicy.ToString(), Is.EqualTo("default-src 'none'; frame-ancestors 'none'"));
        Assert.That(response.Headers.XFrameOptions.ToString(), Is.EqualTo("DENY"));
        Assert.That(response.Headers.XContentTypeOptions.ToString(), Is.EqualTo("nosniff"));
    }

    [Test]
    public void HtmlResponse_StaysFrameableByStudio()
    {
        var response = new DefaultHttpContext().Response;
        response.ContentType = "text/html; charset=utf-8";
        ResponseSecurityHeaders.Apply(response, Studio);
        Assert.That(response.Headers.ContentSecurityPolicy.ToString(), Is.EqualTo("frame-ancestors 'self' https://studio.example.com"));
        Assert.That(response.Headers.ContainsKey("X-Frame-Options"), Is.False);
    }

    [Test]
    public void ResponseOwnHeaders_AreKept()
    {
        var response = new DefaultHttpContext().Response;
        response.ContentType = "text/html";
        response.Headers.ContentSecurityPolicy = "default-src 'self'";
        response.Headers.XContentTypeOptions = "nosniff";
        ResponseSecurityHeaders.Apply(response, Studio);
        Assert.That(response.Headers.ContentSecurityPolicy.ToString(), Is.EqualTo("default-src 'self'"));
        Assert.That(response.Headers.ContainsKey("X-Frame-Options"), Is.False);
    }
}
