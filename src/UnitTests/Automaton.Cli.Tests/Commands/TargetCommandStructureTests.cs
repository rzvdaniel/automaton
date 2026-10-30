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
    public void DeploymentTargetCommands_DoNotExposeRetiredPrivilegedHostApproval()
    {
        var target = TargetCommand.Build(new ConfigService());
        var deploy = DeployCommand.Build(new ConfigService());

        Assert.Multiple(() =>
        {
            Assert.That(target.Subcommands.Select(item => item.Name), Does.Not.Contain("approve"));
            Assert.That(target.Subcommands.Select(item => item.Name), Does.Not.Contain("revoke"));
            Assert.That(target.Subcommands.Select(item => item.Name), Does.Not.Contain("approve-privileged-host"));
            Assert.That(target.Subcommands.Select(item => item.Name), Does.Not.Contain("revoke-privileged-host"));
            Assert.That(
                target.Subcommands.SelectMany(item => item.Options).Any(option => option.Name == "acknowledge-privileged-host-access"),
                Is.False);
            Assert.That(
                deploy.Options.Any(option => option.Name == "acknowledge-privileged-host-access"),
                Is.False);
        });
    }

    [Test]
    public void TargetAdd_AllowsCreatingAnUnassignedTarget()
    {
        var add = TargetCommand.Build(new ConfigService()).Subcommands.Single(item => item.Name == "add");
        var runner = add.Options.Single(option => option.Name == "runner");

        Assert.That(runner.IsRequired, Is.False);
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
