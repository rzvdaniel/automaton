using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public sealed class DeployBuildProcessTests
{
    [Test]
    public void CreateBuildProcessStartInfo_UsesStructuredArgumentsEnvironmentAndNoShellByDefault()
    {
        var startInfo = DeployService.CreateBuildProcessStartInfo(
            new ApplicationCommandSpec
            {
                Executable = "arbitrary-builder",
                Arguments = ["argument with spaces", "$(not-a-shell-expression)"],
                WorkingDirectory = "build",
                Environment = new Dictionary<string, string> { ["BUILD_MODE"] = "release" }
            },
            "C:\\workspace\\build");

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.FileName, Is.EqualTo("arbitrary-builder"));
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { "argument with spaces", "$(not-a-shell-expression)" }));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo("C:\\workspace\\build"));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.Environment["BUILD_MODE"], Is.EqualTo("release"));
        });
    }
}
