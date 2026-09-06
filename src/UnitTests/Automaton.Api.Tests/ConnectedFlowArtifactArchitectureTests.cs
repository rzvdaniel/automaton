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
        var members = typeof(ConnectedFlowArtifactService)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member.ToString() ?? string.Empty)
            .ToArray();

        Assert.That(members, Has.None.Contains("UploadControllerScript"));
        Assert.That(members, Has.None.Contains("FastAPI"));
        Assert.That(members, Has.None.Contains("LegacyControllerRuntimeDeployed"));
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

    [Test]
    public void NullAndEmptyFlowSetsBothProduceAnEmptyIndex()
    {
        var nullFiles = ConnectedFlowArtifactService.BuildArtifactFiles(null);
        var emptyFiles = ConnectedFlowArtifactService.BuildArtifactFiles([]);

        Assert.Multiple(() =>
        {
            Assert.That(nullFiles.Select(file => file.RelativePath), Is.EqualTo(new[] { "index.json" }));
            Assert.That(emptyFiles.Select(file => file.RelativePath), Is.EqualTo(new[] { "index.json" }));
            Assert.That(JsonSerializer.Deserialize<JsonElement>(nullFiles[0].Content).GetArrayLength(), Is.Zero);
            Assert.That(JsonSerializer.Deserialize<JsonElement>(emptyFiles[0].Content).GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public void MatchingSynchronizationResponseIdentityIsAccepted()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        Assert.DoesNotThrow(() => ConnectedFlowArtifactService.ValidateSynchronizationResponse(
            appId,
            targetId,
            new SynchronizeApplicationManagedArtifactsResult(appId, targetId, true)));
    }

    [Test]
    public void MismatchedSynchronizationResponseTargetIsRejected()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConnectedFlowArtifactService.ValidateSynchronizationResponse(
                appId,
                targetId,
                new SynchronizeApplicationManagedArtifactsResult(appId, Guid.NewGuid(), true)));

        Assert.That(exception!.Message, Does.Contain("unexpected deployment target"));
    }

    [Test]
    public void MismatchedSynchronizationResponseAppIsRejected()
    {
        var targetId = Guid.NewGuid();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConnectedFlowArtifactService.ValidateSynchronizationResponse(
                Guid.NewGuid(),
                targetId,
                new SynchronizeApplicationManagedArtifactsResult(Guid.NewGuid(), targetId, true)));

        Assert.That(exception!.Message, Does.Contain("unexpected app"));
    }
}
