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
            new("linear:ENG-2", RunStates.Working, 1, now),
            new("jira:TEAM-42", RunStates.RetryQueued, 2, now)]));
        var query = new FileOrchestratorStatusQuery(file.Path);

        Assert.That((await query.GetRunAsync("linear:ENG-2", CancellationToken.None))?.RunState, Is.EqualTo(RunStates.Working));
        Assert.That(await query.GetRunAsync("missing", CancellationToken.None), Is.Null);
    }

    [TestCase("{\"schemaVersion\":1,\"generatedAtUtc\":\"2026-08-12T00:00:00Z\",\"daemonState\":\"running\",\"repositoryIdentity\":\"acme/widgets\",\"applicationVersion\":\"1.0.0\",\"buildRevision\":\"abcdef0\"}")]
    [TestCase("{\"schemaVersion\":1,\"generatedAtUtc\":\"2026-08-12T00:00:00Z\",\"daemonState\":null,\"repositoryIdentity\":null,\"applicationVersion\":null,\"buildRevision\":null,\"runs\":[]}")]
    public async Task MissingRequiredSchemaOneFieldsAreUnavailable(string json)
    {
        using var file = new TemporaryStatus();
        await File.WriteAllTextAsync(file.Path, json);
        var result = await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None);
        Assert.That(result.Availability, Is.EqualTo(OrchestratorAvailabilityStates.Unavailable));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(null)]
    public async Task NullOrBlankWorkItemIdentityIsUnavailable(string? workItemId)
    {
        using var file = new TemporaryStatus();
        await file.WriteRawAsync(SnapshotJson($"{{\"workItemId\":{JsonSerializer.Serialize(workItemId)},\"runState\":\"working\",\"attempt\":0,\"updatedAtUtc\":\"2026-08-12T00:00:00Z\"}}"));
        var result = await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None);
        Assert.That(result.Availability, Is.EqualTo(OrchestratorAvailabilityStates.Unavailable));
    }

    [Test]
    public async Task DuplicateOrdinalWorkItemIdentityIsUnavailableAndLookupDoesNotThrow()
    {
        using var file = new TemporaryStatus();
        var run = "{\"workItemId\":\"github:1\",\"runState\":\"working\",\"attempt\":0,\"updatedAtUtc\":\"2026-08-12T00:00:00Z\"}";
        await file.WriteRawAsync(SnapshotJson(run + "," + run));
        var query = new FileOrchestratorStatusQuery(file.Path);
        var result = await query.GetAsync(CancellationToken.None);
        var selected = await query.GetRunAsync("github:1", CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Availability, Is.EqualTo(OrchestratorAvailabilityStates.Unavailable)); Assert.That(selected, Is.Null); });
    }

    [TestCase("abc")]
    [TestCase("gggggggggggggggggggggggggggggggggggggggg")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidExactShaIsUnavailable(string sha)
    {
        using var file = new TemporaryStatus();
        var run = $"{{\"workItemId\":\"github:1\",\"runState\":\"working\",\"attempt\":0,\"updatedAtUtc\":\"2026-08-12T00:00:00Z\",\"headSha\":\"{sha}\"}}";
        await file.WriteRawAsync(SnapshotJson(run));
        Assert.That((await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None)).Availability, Is.EqualTo(OrchestratorAvailabilityStates.Unavailable));
    }

    [Test]
    public async Task ValidEmptyRunCollectionIsAvailable()
    {
        using var file = new TemporaryStatus();
        await file.WriteRawAsync(SnapshotJson(string.Empty));
        var result = await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Availability, Is.EqualTo(OrchestratorAvailabilityStates.Available)); Assert.That(result.Status!.Runs, Is.Empty); });
    }

    [TestCase("{\"workItemId\":\"github:1\",\"runState\":\" \",\"attempt\":0,\"updatedAtUtc\":\"2026-08-12T00:00:00Z\"}")]
    [TestCase("{\"workItemId\":\"github:1\",\"runState\":\"working\",\"attempt\":-1,\"updatedAtUtc\":\"2026-08-12T00:00:00Z\"}")]
    [TestCase("null")]
    public async Task StructurallyInvalidRunIsUnavailable(string run)
    {
        using var file = new TemporaryStatus();
        await file.WriteRawAsync(SnapshotJson(run));
        Assert.That((await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None)).Availability, Is.EqualTo(OrchestratorAvailabilityStates.Unavailable));
    }

    [Test]
    public async Task OversizedStatusRemainsUnavailable()
    {
        using var file = new TemporaryStatus();
        await file.WriteRawAsync(new string('x', 2 * 1024 * 1024 + 1));
        Assert.That((await new FileOrchestratorStatusQuery(file.Path).GetAsync(CancellationToken.None)).Availability, Is.EqualTo(OrchestratorAvailabilityStates.Unavailable));
    }

    [Test]
    public void CallerCancellationPropagates()
    {
        using var file = new TemporaryStatus();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await new FileOrchestratorStatusQuery(file.Path).GetAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    private static string SnapshotJson(string runs) => $$"""
        {"schemaVersion":1,"generatedAtUtc":"2026-08-12T00:00:00Z","daemonState":"running","startupReconciled":true,"healthy":true,"repositoryIdentity":"acme/widgets","runs":[{{runs}}],"applicationVersion":"1.0.0","buildRevision":"abcdef0"}
        """;

    private sealed class TemporaryStatus : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "automaton-integration-" + Guid.NewGuid().ToString("N"));
        public TemporaryStatus() { Directory.CreateDirectory(directory); Path = System.IO.Path.Combine(directory, "status.json"); }
        public string Path { get; }
        public Task WriteAsync(OperatorStatusSnapshot value) => File.WriteAllTextAsync(Path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        public Task WriteRawAsync(string value) => File.WriteAllTextAsync(Path, value);
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
