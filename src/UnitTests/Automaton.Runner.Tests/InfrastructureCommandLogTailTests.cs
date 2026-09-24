using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class InfrastructureCommandLogTailTests
{
    [Test]
    public async Task ReadLogTailKeepsOnlyTheBoundedTail()
    {
        var logs = new string('x', 200_000) + "last";

        var result = await InfrastructureCommandService.ReadLogTailAsync(
            new StringReader(logs),
            CancellationToken.None);

        Assert.That(result, Is.EqualTo(logs[^65_536..]));
    }
}
