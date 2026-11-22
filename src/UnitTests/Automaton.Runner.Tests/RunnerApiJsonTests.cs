using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Runner.Console.Models;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerApiJsonTests
{
    [Test]
    public void ReadsRunnerDetailsTheWayTheApiWritesThem()
    {
        var id = Guid.NewGuid();
        var json = $"{{\"id\":\"{id}\",\"name\":\"runner-1\",\"status\":\"Online\"}}";

        var details = JsonSerializer.Deserialize<RunnerDetails>(json, RunnerApiJson.Options);

        Assert.That(details!.Id, Is.EqualTo(id));
        Assert.That(details.Name, Is.EqualTo("runner-1"));
        Assert.That(details.Status, Is.EqualTo(RunnerStatus.Online));
    }
}
