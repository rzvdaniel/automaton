using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class StaticSiteDeployTests
{
    [Test]
    public async Task StaticSiteWithoutRuntimeOrActionsBuildsOnRunner()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-static-deploy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fixture = new DeployServiceTestProject(root);
            fixture.Configure(runtimeCommand: false, includeBackend: false);
            Directory.Delete(Path.Combine(root, "frontend", "dist"), recursive: true);
            var service = fixture.CreateService(new DeployRuntimeResult(true, 1, "static site published"));

            var result = await service.DeployAsync(root, clean: false, directoryOverride: null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True);
                Assert.That(fixture.DeployCalls, Is.EqualTo(1));
                Assert.That(Directory.Exists(Path.Combine(root, "frontend", "dist")), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
