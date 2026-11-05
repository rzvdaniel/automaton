using System.Text.Json;
using Automaton.Api.Extensions;

namespace Automaton.Api.Tests;

public sealed class HealthResponseTests
{
    [TestCase("0.2.13", "server-v0.2.13")]
    [TestCase("server-v0.2.13", "server-v0.2.13")]
    [TestCase(" 0.2.13 ", "server-v0.2.13")]
    public void ReportsTheReleaseTagAndNothingElse(string installed, string expected)
        => Assert.That(JsonSerializer.Serialize(ApiApplicationPipelineExtensions.HealthResponse(installed)),
            Is.EqualTo($"{{\"status\":\"ok\",\"version\":\"{expected}\"}}"));

    [TestCase(null)]
    [TestCase("")]
    public void ReportsOnlyStatusWhenTheVersionIsUnknown(string? installed)
        => Assert.That(JsonSerializer.Serialize(ApiApplicationPipelineExtensions.HealthResponse(installed)),
            Is.EqualTo("{\"status\":\"ok\"}"));
}
