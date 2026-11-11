using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppFramingPolicyTests
{
    private const string Studio = "https://studio.example.com";

    private static Dictionary<string, string[]> Headers(string name, string value) => new() { [name] = [value] };

    
    [TestCase("X-Frame-Options", "DENY", true)]
    [TestCase("x-frame-options", "deny", true)]
    [TestCase("X-Frame-Options", "SAMEORIGIN", true)]
    [TestCase("content-security-policy", "default-src 'self'; frame-ancestors 'none'", true)]
    [TestCase("Content-Security-Policy", "frame-ancestors 'self'", true)]
    [TestCase("Content-Security-Policy", "frame-ancestors https://other.example.com", true)]
    [TestCase("Content-Security-Policy", "frame-ancestors https://studio.example.com", false)]
    [TestCase("Content-Security-Policy", "frame-ancestors studio.example.com", false)]
    [TestCase("Content-Security-Policy", "frame-ancestors *.example.com", false)]
    [TestCase("Content-Security-Policy", "frame-ancestors *", false)]
    [TestCase("Content-Security-Policy", "default-src 'self'", false)]
    public void Reads_the_framing_rules(string header, string value, bool forbidden)
        => Assert.That(AppFramingPolicy.ForbidsFraming(Headers(header, value), Studio), Is.EqualTo(forbidden));

    [Test]
    public void Allows_framing_when_the_app_sends_no_framing_headers()
        => Assert.That(AppFramingPolicy.ForbidsFraming(new Dictionary<string, string[]> { ["Content-Type"] = ["text/html"] }, Studio), Is.False);
}
