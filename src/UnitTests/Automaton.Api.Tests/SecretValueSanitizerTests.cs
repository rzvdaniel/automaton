using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class SecretValueSanitizerTests
{
    [Test]
    public void Sanitize_RemovesRawAndJsonEscapedRepresentations()
    {
        const string secret = "re_value-with-\"quotes\"-and-newline\nend";
        var sanitizer = new SecretValueSanitizer([secret]);
        var jsonEscaped = System.Text.Json.JsonSerializer.Serialize(secret)[1..^1];

        var sanitized = sanitizer.Sanitize($"raw={secret}; json={jsonEscaped}");

        Assert.That(sanitized, Does.Not.Contain(secret));
        Assert.That(sanitized, Does.Not.Contain(jsonEscaped));
        Assert.That(sanitized, Does.Contain("[REDACTED]"));
        Assert.That(sanitizer.SanitizeNullable(null), Is.Null);
    }
}
