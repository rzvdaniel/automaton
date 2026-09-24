using Automaton.Core.Services;
using Automaton.Core.Models;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class DeployServiceTargetTests
{
    private string? originalServerEnv;
    private string? originalApiKeyEnv;

    [SetUp]
    public void SetUp()
    {
        originalServerEnv = Environment.GetEnvironmentVariable("AUTOMATON_SERVER");
        originalApiKeyEnv = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", null);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER", originalServerEnv);
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", originalApiKeyEnv);
    }

    [Test]
    public void CreateBuildProcessStartInfo_UsesStructuredArgumentsEnvironmentAndNoShellByDefault()
    {
        var startInfo = ApplicationBuildExecutor.CreateStartInfo(
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

    [Test]
    public async Task DeployAsync_NamedTargetPassesSlugAndReportsDeploymentIdentity()
    {
        await DeployServiceTargetScenario.RunAsync();
    }
}
