using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Integration;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class OrchestratorIntegrationBoundaryTests
{
    [Test]
    public async Task QueryMapsProviderNeutralIdentityAndLifecycleState()
    {
        using var file = new TemporaryStatus();
        var now = DateTimeOffset.Parse("2026-08-12T12:00:00Z");
        var run = new OperatorRunStatus("jira:TEAM-42", RunStates.Blocked, 2, now, "human-feedback", "reviewer", "key", "/work/key", "automaton/work/key", "https://provider.test/pr/7", new string('a', 40), CiStatuses.Passed, ReviewFeedbackStatuses.Actionable, WorkspaceExecutionOwnershipStates.Quarantined, Reason: "manual attention");
        await file.WriteAsync(new(1, now, OperatorDaemonStates.Running, true, true, "acme/widgets", [run], ApplicationVersion: "2.0.0", BuildRevision: "abcdef0"));

        var result = await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.SchemaVersion, Is.EqualTo(OrchestratorIntegrationSchema.CurrentVersion));
            Assert.That(result.Availability, Is.EqualTo(OrchestratorAvailabilityStates.Available));
            Assert.That(result.Status!.ApplicationVersion, Is.EqualTo("2.0.0"));
            Assert.That(result.Status.BuildRevision, Is.EqualTo("abcdef0"));
            Assert.That(result.Status.Runs.Single().WorkItemId, Is.EqualTo("jira:TEAM-42"));
            Assert.That(result.Status.Runs.Single().OwnershipStatus, Is.EqualTo(WorkspaceExecutionOwnershipStates.Quarantined));
            Assert.That(result.Status.Runs.Single().HeadSha, Has.Length.EqualTo(40));
        });
    }

    [Test]
    public async Task MissingMalformedOrUnsupportedStatusIsUnavailableWithoutThrowing()
    {
        using var file = new TemporaryStatus();
        var missing = await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None);
        await File.WriteAllTextAsync(file.Path, "not-json");
        var malformed = await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None);
        await file.WriteAsync(new(99, DateTimeOffset.UtcNow, OperatorDaemonStates.Running, true, true, "acme/widgets", []));
        var unsupported = await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None);

        Assert.That(new[] { missing, malformed, unsupported }, Has.All.Property(nameof(OrchestratorStatusQueryResult.Availability)).EqualTo(OrchestratorAvailabilityStates.Unavailable));
    }

    [Test]
    public async Task StableWorkItemIdSelectsOneRunWithoutProviderAssumptions()
    {
        using var file = new TemporaryStatus();
        var now = DateTimeOffset.UtcNow;
        await file.WriteAsync(new(1, now, OperatorDaemonStates.Running, true, true, "acme/widgets", [
            new("github:1", RunStates.Completed, 1, now),
            new("linear:ENG-2", RunStates.Working, 1, now)]));
        var query = new FileOrchestratorStatusQuery(file.Path);

        Assert.That((await query.GetRunAsync("linear:ENG-2", CancellationToken.None))?.RunState, Is.EqualTo(RunStates.Working));
        Assert.That(await query.GetRunAsync("missing", CancellationToken.None), Is.Null);
    }

    private sealed class TemporaryStatus : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "automaton-integration-" + Guid.NewGuid().ToString("N"));
        public TemporaryStatus() { Directory.CreateDirectory(directory); Path = System.IO.Path.Combine(directory, "status.json"); }
        public string Path { get; }
        public Task WriteAsync(OperatorStatusSnapshot value) => File.WriteAllTextAsync(Path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
