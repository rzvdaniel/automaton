using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class ExecutionFencingTests
{
    [TestCase(ExecutionFencingKinds.WindowsJob, null, ExecutionIdentityClassifications.Dead)]
    [TestCase(ExecutionFencingKinds.PortableProcessTree, null, ExecutionIdentityClassifications.Ambiguous)]
    [TestCase("unknown-fence", null, ExecutionIdentityClassifications.Ambiguous)]
    [TestCase(ExecutionFencingKinds.UnixProcessGroup, true, ExecutionIdentityClassifications.Dead)]
    [TestCase(ExecutionFencingKinds.UnixProcessGroup, false, ExecutionIdentityClassifications.Ambiguous)]
    [TestCase(ExecutionFencingKinds.UnixProcessGroup, null, ExecutionIdentityClassifications.Ambiguous)]
    public async Task MissingRootClassificationFailsClosedForDescendantAmbiguity(
        string fencingKind,
        bool? processGroupAbsent,
        string expectedClassification)
    {
        var inspector = new FakeRestartProcessInspector(
            new ExecutionRootInspection(ExecutionRootInspectionStates.Missing),
            processGroupAbsent);
        var fence = new PlatformExecutionFence(inspector);
        var identity = new DurableExecutionIdentity(
            42,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "nonce",
            fencingKind,
            fencingKind == ExecutionFencingKinds.UnixProcessGroup ? 42 : null);

        var result = await fence.InspectAsync(identity, CancellationToken.None);

        Assert.That(result.Classification, Is.EqualTo(expectedClassification));
    }

    [Test]
    public async Task LiveRootStillUsesStartTimeForPidReuseProtection()
    {
        var inspector = new FakeRestartProcessInspector(
            new ExecutionRootInspection(ExecutionRootInspectionStates.Live, DateTimeOffset.Parse("2026-01-02T00:00:00Z")),
            null);
        var fence = new PlatformExecutionFence(inspector);
        var identity = new DurableExecutionIdentity(
            42,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            "nonce",
            ExecutionFencingKinds.WindowsJob);

        var result = await fence.InspectAsync(identity, CancellationToken.None);

        Assert.That(result.Classification, Is.EqualTo(ExecutionIdentityClassifications.Reused));
    }

    private sealed class FakeRestartProcessInspector(ExecutionRootInspection root, bool? processGroupAbsent) : IRestartProcessInspector
    {
        public ExecutionRootInspection InspectRoot(int processId) => root;
        public bool? IsProcessGroupAbsent(int processGroupId) => processGroupAbsent;
    }
}
