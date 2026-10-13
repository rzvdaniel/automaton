using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationMySqlIntegrationTests
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Test]
    public async Task Initial_zero_sentinel_is_allocated_as_one_and_same_request_replays_immutable_receipt()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var request = CreateRequest(fixture, Guid.NewGuid(), 0);
        var bytes = Canonicalize(request);
        NodeActivationResult result;
        await using (var db = fixture.CreateContext())
            result = await fixture.CreateActivationService(db).ActivateAsync(Identity(fixture), request, bytes, CancellationToken.None);
        Assert.That(result.Disposition, Is.EqualTo(NodeActivationDisposition.Accepted));

        NodeActivationResult retry;
        await using (var db = fixture.CreateContext())
            retry = await fixture.CreateActivationService(db).ActivateAsync(Identity(fixture), request, bytes, CancellationToken.None);
        var stored = await LoadManagementAsync(fixture);
        var attemptCount = await CountAttemptsAsync(fixture);
        var receipt = JsonSerializer.Deserialize<NodeActivationReceipt>(result.ImmutableReceiptJson!, CanonicalOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(retry.Disposition, Is.EqualTo(NodeActivationDisposition.Accepted));
            Assert.That(retry.ImmutableReceiptJson, Is.EqualTo(result.ImmutableReceiptJson));
            Assert.That(stored.CurrentIncarnation, Is.EqualTo(1));
            Assert.That(stored.Status, Is.EqualTo(NodeManagementStatuses.Recovering));
            Assert.That(receipt.Incarnation, Is.EqualTo(1));
            Assert.That(attemptCount, Is.EqualTo(1));
        });

        var conflictRequest = request with { JournalInventorySha256 = new string('f', 64) };
        await using var conflictDb = fixture.CreateContext();
        var conflict = await fixture.CreateActivationService(conflictDb)
            .ActivateAsync(Identity(fixture), conflictRequest, Canonicalize(conflictRequest), CancellationToken.None);
        Assert.That(conflict.ErrorCode, Is.EqualTo("activation_request_conflict"));
    }

    [Test]
    public async Task Independent_contexts_racing_initial_cas_allocate_exactly_one_incarnation()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var requests = new[] { CreateRequest(fixture, Guid.NewGuid(), 0), CreateRequest(fixture, Guid.NewGuid(), 0) };
        var results = await RaceAsync(
            () => ActivateInOwnContextAsync(fixture, requests[0]),
            () => ActivateInOwnContextAsync(fixture, requests[1]));
        var state = await LoadManagementAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(value => value.Disposition == NodeActivationDisposition.Accepted), Is.EqualTo(1));
            Assert.That(results.Count(value => value.Disposition == NodeActivationDisposition.Conflict), Is.EqualTo(1));
            Assert.That(state.CurrentIncarnation, Is.EqualTo(1));
            Assert.That(state.Status, Is.EqualTo(NodeManagementStatuses.Recovering));
        });
    }

    [Test]
    public async Task Actual_migration_enforces_positive_incarnations_and_epoch_values()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        await using var db = fixture.CreateContext();
        db.NodeManagement.Add(new NodeManagement
        {
            NodeId = fixture.NodeId, InstallationId = fixture.InstallationId,
            EnrollmentAuthorizationId = fixture.EnrollmentId, ControlPlaneRealmId = fixture.RealmId,
            CredentialRevision = 1, RecoveryEpoch = 1, CurrentIncarnation = 0,
            Status = NodeManagementStatuses.Enrolled
        });
        Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
    }

    private static async Task<NodeActivationResult> ActivateInOwnContextAsync(NodeEnrollmentMySqlTestFixture fixture,
        NodeActivationRequest request, INodeHostFenceReceiptValidator? validator = null)
    {
        await using var db = fixture.CreateContext();
        return await fixture.CreateActivationService(db, validator)
            .ActivateAsync(Identity(fixture), request, Canonicalize(request), CancellationToken.None);
    }

    private static NodeActivationRequest CreateRequest(NodeEnrollmentMySqlTestFixture fixture, Guid requestId,
        long expectedIncarnation) => new(1, fixture.RealmId, 3, fixture.NodeId, fixture.InstallationId,
        requestId, expectedIncarnation, 1, new string('1', 64), new string('2', 64), [1], null);

    private static NodeIdentityResult Identity(NodeEnrollmentMySqlTestFixture fixture) =>
        NodeIdentityResult.Success(fixture.NodeId, fixture.InstallationId, fixture.RealmId);

    private static byte[] Canonicalize(NodeActivationRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, CanonicalOptions);

    private static async Task<NodeManagement> LoadManagementAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using var db = fixture.CreateContext();
        return await db.NodeManagement.AsNoTracking().SingleAsync(value => value.NodeId == fixture.NodeId);
    }

    private static async Task<int> CountAttemptsAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using var db = fixture.CreateContext();
        return await db.NodeActivationAttempts.CountAsync();
    }

    private static async Task<T[]> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[] { Task.Run(async () => { await start.Task; return await first(); }),
            Task.Run(async () => { await start.Task; return await second(); }) };
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

}
