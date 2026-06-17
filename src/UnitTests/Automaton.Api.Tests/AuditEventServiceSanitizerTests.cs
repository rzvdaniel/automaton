using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public class AuditEventServiceSanitizerTests
{
    [Test]
    public void SanitizeMetadataJson_RedactsSensitiveKeysCaseInsensitively()
    {
        var json = AuditEventService.SanitizeMetadataJson(new
        {
            Api_Key = "secret-api-key",
            RawKey = "raw-secret",
            ACCESS_TOKEN = "token-secret",
            ConnectionString = "Server=db;Password=secret;",
            Authorization = "Bearer topsecret",
            SafeName = "visible"
        });

        Assert.That(json, Does.Not.Contain("secret-api-key"));
        Assert.That(json, Does.Not.Contain("raw-secret"));
        Assert.That(json, Does.Not.Contain("token-secret"));
        Assert.That(json, Does.Not.Contain("Server=db"));
        Assert.That(json, Does.Not.Contain("Bearer topsecret"));
        Assert.That(json, Does.Contain("[REDACTED]"));
        Assert.That(json, Does.Contain("visible"));
    }
}
