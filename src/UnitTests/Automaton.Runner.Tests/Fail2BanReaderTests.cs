using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class Fail2BanReaderTests
{
    [Test]
    public void HelperOutput_ParsesToJailsAndCurrentBans()
    {
        var json = """
            {"running": true, "jails": [{"name": "sshd", "enabled": true, "lastHour": 2, "last24Hours": 5,
              "bans": [{"ip": "203.0.113.9", "at": 1763500000, "until": 1763503600}, {"ip": "bad", "at": 1, "until": null},
                       {"ip": "2001:db8::1", "at": 1763500000, "until": null}]}]}
            """;
        var status = Fail2BanReader.Parse(json, DateTime.UtcNow)!;
        Assert.Multiple(() =>
        {
            Assert.That(status.Running, Is.True);
            Assert.That(status.Jails.Single().BannedLast24Hours, Is.EqualTo(5));
            Assert.That(status.Jails.Single().CurrentBans.Select(ban => ban.Ip), Is.EqualTo(new[] { "203.0.113.9", "2001:db8::1" }), "invalid address dropped");
            Assert.That(status.Jails.Single().CurrentBans[1].ExpiresAtUtc, Is.Null);
        });
    }

    [Test]
    public void AbsentFail2Ban_IsOffWithNoJails_AndGarbageIsUnknown()
    {
        Assert.That(Fail2BanReader.Parse("""{"running": false, "jails": []}""", DateTime.UtcNow)!.Running, Is.False);
        Assert.That(Fail2BanReader.Parse("""{"error": true}""", DateTime.UtcNow), Is.Null, "an unreadable database is unknown, not no bans");
        Assert.That(Fail2BanReader.Parse("not json", DateTime.UtcNow), Is.Null);
        Assert.That(Fail2BanReader.Parse(null, DateTime.UtcNow), Is.Null);
    }
}
