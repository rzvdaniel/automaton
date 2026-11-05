using Automaton.Core.Models;

namespace Automaton.Core.Tests.Models;

[TestFixture]
public class ApplicationInstallManifestTests
{
    private static ApplicationContract Parse(string application)
        => ApplicationContractSerialization.Deserialize(application);

    [Test]
    public void Settings_and_schedules_round_trip()
    {
        var contract = Parse("""
            { "settings": [ { "name": "S3_REGION", "description": "Region", "default": "us-east-1", "required": true } ],
              "schedules": [ { "name": "Nightly", "action": "upload", "cron": "30 3 * * *", "timeZone": "Etc/UTC", "input": { "dryRun": false } } ] }
            """);
        Assert.That(contract.Settings[0].Default, Is.EqualTo("us-east-1"));
        Assert.That(contract.Schedules[0].Action, Is.EqualTo("upload"));
        var again = ApplicationContractSerialization.Deserialize(ApplicationContractSerialization.Serialize(contract));
        Assert.That(again.Schedules[0].Cron, Is.EqualTo("30 3 * * *"));
    }

    [TestCase("""{ "settings": [ { "name": "lower" } ] }""", "settings[].name")]
    [TestCase("""{ "settings": [ { "name": "A" }, { "name": "A" } ] }""", "more than once")]
    [TestCase("""{ "settings": [ { "name": "A" } ], "secrets": [ { "name": "A" } ] }""", "also declared as a secret")]
    [TestCase("""{ "schedules": [ { "name": "x", "action": "Bad Key", "cron": "* * * * *" } ] }""", "valid Action key")]
    [TestCase("""{ "schedules": [ { "name": "x", "action": "upload" } ] }""", "exactly one of cron or intervalSeconds")]
    [TestCase("""{ "schedules": [ { "name": "x", "action": "upload", "cron": "* *" } ] }""", "five fields")]
    [TestCase("""{ "schedules": [ { "name": "x", "action": "upload", "cron": "* * * * *", "intervalSeconds": 60 } ] }""", "exactly one")]
    [TestCase("""{ "schedules": [ { "name": "x", "action": "upload", "intervalSeconds": 60, "input": [1] } ] }""", "must be an object")]
    public void Invalid_manifests_are_rejected(string json, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Parse(json));
        Assert.That(ex!.Message, Does.Contain(expected));
    }
}
