using Automaton.Core.Models;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HttpDeploymentRoutingTests
{
    [TestCase("static", true)]
    [TestCase("compose", true)]
    [TestCase("finite", false)]
    public async Task DeploymentFinalizesRoutesForHttpWorkloadsWithoutARuntimeCommand(string kind, bool routable)
    {
        await using var fixture = await ApplicationDeploymentServiceTests.DeploymentFixture.CreateAsync(initialGenericMarker: true);
        var package = DeploymentPackageArchiveFactory.Create(fixture.AppId, fixture.PackagePath,
            hasRuntime: false, application: Contract(kind));

        var response = await fixture.Service.DeployPackageAsync(fixture.AppId, fixture.OwnerId,
            fixture.Target.Slug, package, clean: false, CancellationToken.None);
        var target = await DeploymentTargetSnapshot.ReadAsync(fixture.Db, fixture.Target.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.Success, Is.True);
            Assert.That(target.GenericRuntimeDeployed, Is.EqualTo(routable));
            Assert.That(File.Exists(fixture.RouteFilePath), Is.EqualTo(routable));
        });
        if (routable)
            Assert.That(await File.ReadAllTextAsync(fixture.RouteFilePath), Does.Contain($"app-{fixture.AppId:N}"));
    }

    private static ApplicationContract Contract(string kind)
    {
        var contract = new ApplicationContract();
        if (kind == "static")
        {
            contract.Build.Image = "test/build:1";
            contract.Build.Command = new ApplicationCommandSpec { Executable = "build" };
            contract.Build.OutputDirectory = "dist";
        }
        else if (kind == "compose")
        {
            contract.Compose = new ApplicationComposeSpec
            {
                PublicService = "web",
                Services = [new ApplicationComposeService { Name = "web", Image = "test/web:1", Port = 8080 }]
            };
        }
        return contract;
    }
}
