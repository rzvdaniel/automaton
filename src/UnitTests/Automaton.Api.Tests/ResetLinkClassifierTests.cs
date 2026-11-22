using Automaton.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Automaton.Api.Tests;

public class ResetLinkClassifierTests
{
    private static string Code(IDataProtectionProvider provider, DateTimeOffset created)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(created.UtcTicks);
            writer.Write("user");
        }
        var token = provider.CreateProtector("DataProtectorTokenProvider").Protect(stream.ToArray());
        return Convert.ToBase64String(token);
    }

    [Test]
    public void ARecentLinkThatFailsWasUsedAndAnOldOneExpired()
    {
        var provider = new EphemeralDataProtectionProvider();
        var options = new DataProtectionTokenProviderOptions { TokenLifespan = TimeSpan.FromHours(24) };
        var now = DateTimeOffset.UtcNow;

        Assert.That(ResetLinkClassifier.Classify(provider, options, Code(provider, now.AddHours(-1)), now), Is.EqualTo("used"));
        Assert.That(ResetLinkClassifier.Classify(provider, options, Code(provider, now.AddHours(-25)), now), Is.EqualTo("expired"));
    }

    [Test]
    public void AForeignOrMalformedCodeIsInvalid()
    {
        var options = new DataProtectionTokenProviderOptions();
        Assert.That(ResetLinkClassifier.Classify(new EphemeralDataProtectionProvider(), options, "not-a-token", DateTimeOffset.UtcNow), Is.EqualTo("invalid"));
        var other = new EphemeralDataProtectionProvider();
        Assert.That(ResetLinkClassifier.Classify(new EphemeralDataProtectionProvider(), options, Code(other, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow), Is.EqualTo("invalid"));
    }
}
