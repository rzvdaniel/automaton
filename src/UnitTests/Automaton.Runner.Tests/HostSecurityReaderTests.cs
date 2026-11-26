using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class HostSecurityReaderTests
{
    [Test]
    public void HelperOutput_ParsesToFindings()
    {
        var json = """{"findings": [{"id": "ssh-password", "title": "SSH allows passwords", "detail": "d", "fixable": true}, {"id": "", "title": "x", "detail": ""}], "firewall": "ufw", "authorizedKeyUsers": 1}""";
        var status = HostSecurityReader.Parse(json)!;
        Assert.Multiple(() =>
        {
            Assert.That(status.Firewall, Is.EqualTo("ufw"));
            Assert.That(status.Findings.Select(finding => finding.Id), Is.EqualTo(new[] { "ssh-password" }), "a finding without an id is dropped");
        });
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("{}")]
    public void UnreadableOutput_IsUnknownNotAllGood(string output) => Assert.That(HostSecurityReader.Parse(output), Is.Null);
}
