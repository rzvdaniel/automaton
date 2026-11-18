using System.Text.Json;
using Automaton.Api.Extensions;

namespace Automaton.Api.Tests;

public sealed class HealthResponseTests
{
    [Test]
    public void AnonymousHealthReportsOnlyTheStatus()
        => Assert.That(JsonSerializer.Serialize(ApiApplicationPipelineExtensions.HealthResponse()), Is.EqualTo("{\"status\":\"ok\"}"));
}
