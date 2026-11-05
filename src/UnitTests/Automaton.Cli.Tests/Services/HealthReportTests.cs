using NUnit.Framework;
using Automaton.Cli.Services;

namespace Automaton.Cli.Tests.Services;

public sealed class HealthReportTests
{
    [TestCase("ok", "ok", null)]
    [TestCase("{\"status\":\"ok\",\"version\":\"server-v0.2.13\"}", "ok", "server-v0.2.13")]
    [TestCase("{\"status\":\"ok\"}", "ok", null)]
    [TestCase("{not json", "{not json", null)]
    public void ParseHealthReadsOldAndNewServers(string body, string status, string? version)
        => Assert.That(AutomatonApiClient.ParseHealth(body), Is.EqualTo((status, version)));
}
