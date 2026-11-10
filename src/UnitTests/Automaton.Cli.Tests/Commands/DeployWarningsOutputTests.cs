using Automaton.Cli.Commands;
using Automaton.Cli.Models;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class DeployWarningsOutputTests
{
    [Test]
    public void PlainResult_ListsEachWarningUnderTheCompleteLine()
    {
        var result = new DeployResult
        {
            Success = true,
            HasWarnings = true,
            Checks =
            [
                new DeployCheckResult("restore_point", false, "Restore point skipped: nothing to save", IsWarning: true),
                new DeployCheckResult("upload", true, "ok")
            ]
        };

        var output = Capture(() => DeployCommand.WriteResult(result, asJson: false, verbose: false, nonInteractive: true));

        Assert.That(output, Does.Contain("Deploy complete with warnings."));
        Assert.That(output, Does.Contain(" - restore_point: Restore point skipped: nothing to save"));
        Assert.That(output, Does.Not.Contain("upload: ok"));
    }

    private static string Capture(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { action(); } finally { Console.SetOut(original); }
        return writer.ToString();
    }
}
