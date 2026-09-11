using System.Text.Json;
using Automaton.Messaging.Events;
using NUnit.Framework;

namespace Automaton.Worker.Tests;

public sealed class InfrastructureCommandContractTests
{
    [TestCase("provision-hosted-runner")]
    [TestCase("reprovision-hosted-runner")]
    [TestCase("restart-hosted-runner")]
    [TestCase("suspend-hosted-runner")]
    [TestCase("resume-hosted-runner")]
    [TestCase("delete-hosted-runner")]
    [TestCase("measure-hosted-runner-storage")]
    public void ConvertsRequestWithoutTransportOrServerOnlyFields(string actionKey)
    {
        var grants = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var request = new InfrastructureActionExecutionRequestedEvent
        {
            OperationId = Guid.NewGuid(), HostedRunnerId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            RunnerHostId = Guid.NewGuid(), ActionKey = actionKey,
            InputJson = "{\"containerName\":\"hosted-test\",\"memoryMb\":512}",
            SecretGrantIds = grants, CorrelationId = "server-only"
        };
        var infrastructureRunnerId = Guid.NewGuid();
        var createdAtUtc = new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

        var command = request.ToInfrastructureCommand(infrastructureRunnerId, createdAtUtc);
        var json = JsonSerializer.Serialize(command);
        var restored = JsonSerializer.Deserialize<InfrastructureCommandRequestedEvent>(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(restored.CommandId, Is.EqualTo(request.OperationId));
            Assert.That(restored.OperationId, Is.EqualTo(request.OperationId));
            Assert.That(restored.RunnerHostId, Is.EqualTo(request.RunnerHostId));
            Assert.That(restored.InfrastructureRunnerId, Is.EqualTo(infrastructureRunnerId));
            Assert.That(restored.ActionKey, Is.EqualTo(actionKey));
            Assert.That(restored.InputJson, Is.EqualTo(request.InputJson));
            Assert.That(restored.SecretGrantIds, Is.EqualTo(grants));
            Assert.That(restored.CreatedAtUtc, Is.EqualTo(createdAtUtc));
            Assert.That(restored.CreatedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(request.ToInfrastructureCommand(infrastructureRunnerId, createdAtUtc).CommandId,
                Is.EqualTo(command.CommandId));
        });
        using var document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[]
        {
            "CommandId", "OperationId", "RunnerHostId", "InfrastructureRunnerId",
            "ActionKey", "InputJson", "SecretGrantIds", "CreatedAtUtc"
        }));
        Assert.That(document.RootElement.GetProperty("SecretGrantIds").EnumerateArray().Select(value => value.GetGuid()),
            Is.EqualTo(grants));

        grants[0] = Guid.NewGuid();
        Assert.That(command.SecretGrantIds, Is.EqualTo(restored.SecretGrantIds));
    }
}
