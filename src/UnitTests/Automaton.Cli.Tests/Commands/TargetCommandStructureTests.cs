using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

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
            Is.EquivalentTo(new[] { "list", "add", "remove", "primary", "default" }));

        Assert.That(
            command.Subcommands.Single(item => item.Name == "default").IsHidden,
            Is.True);
    }

    [Test]
    public void DeployCommand_AcceptsOptionalTargetArgument()
    {
        var command = DeployCommand.Build(new ConfigService());

        Assert.That(command.Arguments.Select(argument => argument.Name), Does.Contain("target"));
    }

    [TestCase("primary")]
    [TestCase("default")]
    public async Task TargetPromotion_MissingTargetReturnsNonZeroProcessExitCode(string commandName)
    {
        var root = new RootCommand { TargetCommand.Build(new ConfigService()) };
        var parser = new CommandLineBuilder(root).Build();
        var directory = Path.Combine(Path.GetTempPath(), "automaton-target-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Directory.SetCurrentDirectory(directory);
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);

            var exitCode = await parser.InvokeAsync(["target", commandName, "missing-target", "--env", "missing"]);

            Assert.That(exitCode, Is.Not.EqualTo(0));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.Delete(directory, recursive: true);
        }
    }
}
