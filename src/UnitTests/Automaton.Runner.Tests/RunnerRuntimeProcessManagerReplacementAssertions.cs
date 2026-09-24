using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;
using static Automaton.Runner.Console.Tests.RunnerRuntimeProcessManagerTestSupport;

namespace Automaton.Runner.Console.Tests;

internal static class RunnerRuntimeProcessManagerReplacementAssertions
{
    public static void AssertFailedReplacementWasRolledBack(
        string metadataPath,
        RunnerRuntimeRegistry registry,
        Guid appId,
        Guid targetId,
        RunnerRuntimeDeploymentMetadata original,
        int originalPort)
    {
        var restored = ReadMetadata(metadataPath);
        Assert.Multiple(() =>
        {
            Assert.That(IsProcessAlive(original.ProcessId!.Value), Is.True);
            Assert.That(registry.TryGet(appId, targetId, out var route), Is.True);
            Assert.That(route.Port, Is.EqualTo(originalPort));
            Assert.That(restored.State, Is.EqualTo(RuntimeReadinessState.Ready));
            Assert.That(restored.ProcessId, Is.EqualTo(original.ProcessId));
        });
    }
}
