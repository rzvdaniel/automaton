using Automaton.Cli.Commands;
using NUnit.Framework;

namespace Automaton.Cli.Tests;

[NonParallelizable]
public sealed class HomeCardCheckTests
{
    private static (string Err, int Exit) Run(string result)
    {
        var original = Console.Error;
        var writer = new StringWriter();
        Console.SetError(writer);
        Environment.ExitCode = 0;
        try { ActionCommand.ReportCardProblems(result); }
        finally { Console.SetError(original); }
        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        return (writer.ToString(), exit);
    }

    [Test]
    public void ABrokenBlockIsReportedWithItsPathAndFailsTheRun()
    {
        var (err, exit) = Run("""{"status":"a","blocks":[{"type":"counters","items":[{"label":"x","value":1}]}]}""");
        Assert.That(err, Does.Contain("card.blocks[0].items needs 2 to 4 counters"));
        Assert.That(exit, Is.EqualTo(1));
    }

    [Test]
    public void AValidCardPassesAndAnUnknownBlockIsOnlyAWarning()
    {
        var (err, exit) = Run("""{"status":"a","blocks":[{"type":"hologram"}]}""");
        Assert.That(err, Does.Contain("is valid").And.Contain("hologram"));
        Assert.That(exit, Is.Zero);
    }
}
