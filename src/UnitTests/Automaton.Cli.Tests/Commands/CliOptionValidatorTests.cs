using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class CliOptionValidatorTests
{
    [Test]
    public void HasUnsupportedOptions_RejectsUndocumentedDeployYesFlag()
    {
        var command = DeployCommand.Build(new ConfigService());

        Assert.That(CliOptionValidator.HasUnsupportedOptions(["deploy", "--yes"], command), Is.True);
    }

    [Test]
    public void HasUnsupportedOptions_AcceptsSupportedDeployOptionsAndValues()
    {
        var command = DeployCommand.Build(new ConfigService());

        Assert.That(
            CliOptionValidator.HasUnsupportedOptions(
                ["deploy", "--env", "staging", "--publish", "--non-interactive"],
                command),
            Is.False);
    }

    [Test]
    public void HasUnsupportedOptions_AcceptsAnOptionValueThatStartsWithHyphen()
    {
        var command = DeployCommand.Build(new ConfigService());

        Assert.That(CliOptionValidator.HasUnsupportedOptions(["deploy", "--runner", "-local-runner"], command), Is.False);
    }
}
