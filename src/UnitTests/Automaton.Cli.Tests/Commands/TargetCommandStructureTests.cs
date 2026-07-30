using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class TargetCommandStructureTests
{
    [Test]
    public void TargetCommand_ExposesRequiredManagementCommands()
    {
        var command = TargetCommand.Build(new ConfigService());

        Assert.That(
            command.Subcommands.Select(item => item.Name),
            Is.EquivalentTo(new[] { "list", "add", "remove", "default" }));
    }

    [Test]
    public void DeployCommand_AcceptsOptionalTargetArgument()
    {
        var command = DeployCommand.Build(new ConfigService());

        Assert.That(command.Arguments.Select(argument => argument.Name), Does.Contain("target"));
    }
}
