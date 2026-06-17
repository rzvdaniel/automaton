using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public class HostedRunnerRemoteExecutionResultTests
{
    [Test]
    public void ContainerId_ReturnsLastNonEmptyOutputLine()
    {
        var result = new HostedRunnerRemoteExecutionResult(
            true,
            0,
            "network prepared\nabc123container\n",
            string.Empty);

        Assert.That(result.ContainerId, Is.EqualTo("abc123container"));
    }
}
