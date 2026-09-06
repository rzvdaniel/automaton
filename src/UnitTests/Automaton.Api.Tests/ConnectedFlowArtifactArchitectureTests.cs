using Automaton.Api.Services;
using Automaton.Core.Models;
using System.Text.Json;
using System.Reflection;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ConnectedFlowArtifactArchitectureTests
{
    [Test]
    public void ConnectedFlowArtifactServiceIsIndependentOfControllerExecution()
    {
        var constructor = typeof(ConnectedFlowArtifactService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.That(parameterTypes, Does.Not.Contain(typeof(AppsControllerExecutionService)));

        var members = typeof(ConnectedFlowArtifactService)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member.ToString() ?? string.Empty)
            .ToArray();

        Assert.That(members, Has.None.Contains("UploadControllerScript"));
        Assert.That(members, Has.None.Contains("FastAPI"));
        Assert.That(members, Has.None.Contains("LegacyControllerRuntimeDeployed"));
    }

    [Test]
    public void ControllerExecutionServiceNoLongerOwnsConnectedFlowDeployment()
    {
        var members = typeof(AppsControllerExecutionService)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member.ToString() ?? string.Empty)
            .ToArray();

        Assert.That(members, Has.None.Contains("DeployManagedFlowArtifactsAsync"));
        Assert.That(members, Has.None.Contains("managed-flow-artifact"));
    }

    [Test]
    public void ArtifactSetPreservesFlowIdentityAndReplacesRemovedFlows()
    {
        var first = new Flow { Id = Guid.NewGuid(), Name = "First" };
        var second = new Flow { Id = Guid.NewGuid(), Name = "Second" };

        var initial = ConnectedFlowArtifactService.BuildArtifactFiles([first, second]);
        var updated = ConnectedFlowArtifactService.BuildArtifactFiles([second]);

        Assert.Multiple(() =>
        {
            Assert.That(initial.Select(file => file.RelativePath), Is.EquivalentTo(new[]
            {
                $"{first.Id:N}.json", $"{second.Id:N}.json", "index.json"
            }));
            Assert.That(updated.Select(file => file.RelativePath), Is.EquivalentTo(new[]
            {
                $"{second.Id:N}.json", "index.json"
            }));

            var index = JsonSerializer.Deserialize<JsonElement>(
                updated.Single(file => file.RelativePath == "index.json").Content);
            Assert.That(index.GetArrayLength(), Is.EqualTo(1));
            Assert.That(index[0].GetProperty("FlowId").GetGuid(), Is.EqualTo(second.Id));
            Assert.That(index[0].GetProperty("Name").GetString(), Is.EqualTo("Second"));
        });
    }
}
