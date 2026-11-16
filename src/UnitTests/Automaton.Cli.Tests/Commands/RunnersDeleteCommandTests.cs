using Automaton.Cli.Commands;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class RunnersDeleteCommandTests
{
    [Test]
    public void WriteUnwrapped_PrintsLongCommandOnOneLine()
    {
        var command = "docker ps -aq --filter 'label=automaton.runner=abc' | xargs -r docker rm -f && " + new string('x', 200);
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { RunnersDeleteCommand.WriteUnwrapped(command); } finally { Console.SetOut(original); }

        Assert.That(writer.ToString().TrimEnd('\r', '\n'), Is.EqualTo(command));
    }
}
