using Automaton.Api.Models;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppRuntimeLogEventParserTests
{
    [Test]
    public void Parse_WhenResponseContainsCapturedLogs_ShouldNormalizeRuntimeEvents()
    {
        var timestamp = new DateTime(2026, 6, 29, 12, 34, 56, DateTimeKind.Utc);
        var responsePayload = """
            {
              "success": true,
              "logs": [
                "Creating post",
                "ERROR duplicate slug",
                "Traceback: boom"
              ]
            }
            """;

        var events = AppRuntimeLogEventParser.Parse(responsePayload, timestamp);

        Assert.That(events, Has.Count.EqualTo(3));
        Assert.That(events[0].Level, Is.EqualTo(AppRuntimeLogLevels.Info));
        Assert.That(events[0].Stream, Is.EqualTo(AppRuntimeLogStreams.Stdout));
        Assert.That(events[0].Message, Is.EqualTo("Creating post"));
        Assert.That(events[0].Timestamp, Is.EqualTo(timestamp));
        Assert.That(events[1].Level, Is.EqualTo(AppRuntimeLogLevels.Error));
        Assert.That(events[1].Stream, Is.EqualTo(AppRuntimeLogStreams.Stderr));
        Assert.That(events[2].Level, Is.EqualTo(AppRuntimeLogLevels.Error));
        Assert.That(events.All(e => e.Source == AppRuntimeLogSources.Backend), Is.True);
    }

    [Test]
    public void Parse_WhenResponsePayloadIsInvalidJson_ShouldReturnEmptyList()
    {
        var events = AppRuntimeLogEventParser.Parse("not json", DateTime.UtcNow);

        Assert.That(events, Is.Empty);
    }
}
