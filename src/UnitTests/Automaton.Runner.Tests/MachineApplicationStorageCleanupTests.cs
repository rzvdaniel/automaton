using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class MachineApplicationStorageCleanupTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task DeletesOnlyVerifiedAppDataVolumes(bool owned)
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var volume = HostContainerComposeDocumentBuilder.VolumeName(new(Guid.Empty, Guid.Empty, appId, targetId), "tasks");
        var removed = false;
        var cleanup = new MachineApplicationStorageCleanup((args, _) =>
        {
            if (args[2] == "rm") { removed = true; return Success(); }
            if (args[2] == "ls") return Success(volume);
            return Success(VolumeLabels(owned ? appId : Guid.NewGuid(), targetId));
        });

        if (owned) await cleanup.DeleteComposeVolumesAsync(appId);
        else Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.DeleteComposeVolumesAsync(appId));

        Assert.That(removed, Is.EqualTo(owned));
    }

    [Test]
    public async Task BusyVolumeFailsDeletionAndCanBeRetriedWithoutForce()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var volume = HostContainerComposeDocumentBuilder.VolumeName(new(Guid.Empty, Guid.Empty, appId, targetId), "tasks");
        var busy = true;
        var exists = true;
        var cleanup = new MachineApplicationStorageCleanup((args, _) =>
        {
            if (args[2] == "ls") return Success(exists ? volume : "");
            if (args[2] == "inspect") return Success(VolumeLabels(appId, targetId));
            Assert.That(args, Is.EqualTo(new[] { "docker", "volume", "rm", volume }));
            if (busy) return Task.FromResult(new InfrastructureCommandResponse { Error = "volume is in use" });
            exists = false;
            return Success();
        });

        Assert.ThrowsAsync<InvalidOperationException>(() => cleanup.DeleteComposeVolumesAsync(appId));
        Assert.That(exists, Is.True);
        busy = false;
        await cleanup.DeleteComposeVolumesAsync(appId);
        await cleanup.DeleteComposeVolumesAsync(appId);
        Assert.That(exists, Is.False);
    }

    private static string VolumeLabels(Guid appId, Guid targetId) => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["com.automaton.managed"] = "true",
        ["com.automaton.resource"] = "compose-app-volume",
        ["com.automaton.app-id"] = appId.ToString("D"),
        ["com.automaton.deployment-target-id"] = targetId.ToString("D"),
        ["com.automaton.compose-volume"] = "tasks"
    });

    private static Task<InfrastructureCommandResponse> Success(string output = "")
        => Task.FromResult(new InfrastructureCommandResponse { Success = true, Output = output });
}
