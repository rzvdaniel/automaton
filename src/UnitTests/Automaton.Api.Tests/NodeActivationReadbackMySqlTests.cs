using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationReadbackMySqlTests
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Test]
    public async Task No_activation_uses_fresh_authority_epoch_without_creating_management_state()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        await using var db = fixture.CreateContext();
        var service = new NodeActivationReadbackService(db, new TestNodeEpochAuthority());

        var result = await service.ReadCurrentAsync(Identity(fixture), CancellationToken.None);
        var managementRows = await db.NodeManagement.CountAsync();
        var attemptRows = await db.NodeActivationAttempts.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Disposition, Is.EqualTo(NodeActivationReadbackDisposition.Success));
            Assert.That(result.Readback!.Status, Is.EqualTo("NoActivation"));
            Assert.That(result.Readback.RecoveryEpoch, Is.Null);
            Assert.That(result.Readback.AuthoritativeRecoveryEpoch, Is.EqualTo("3"));
            Assert.That(result.Readback.CurrentIncarnation, Is.Null);
            Assert.That(result.Readback.CurrentActivationRequestId, Is.Null);
            Assert.That(managementRows, Is.Zero);
            Assert.That(attemptRows, Is.Zero);
        });
    }

    [Test]
    public async Task Positive_incarnation_readback_returns_exact_immutable_receipt()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var request = CreateInitialRequest(fixture);
        NodeActivationResult activation;
        await using (var db = fixture.CreateContext())
            activation = await fixture.CreateActivationService(db).ActivateAsync(
                Identity(fixture), request, Canonicalize(request), CancellationToken.None);

        await using var readbackDb = fixture.CreateContext();
        var result = await new NodeActivationReadbackService(readbackDb, new TestNodeEpochAuthority())
            .ReadCurrentAsync(Identity(fixture), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(activation.Disposition, Is.EqualTo(NodeActivationDisposition.Accepted));
            Assert.That(result.Disposition, Is.EqualTo(NodeActivationReadbackDisposition.Success));
            Assert.That(result.Readback!.Status, Is.EqualTo(NodeManagementStatuses.Recovering));
            Assert.That(result.Readback.RecoveryEpoch, Is.EqualTo("3"));
            Assert.That(result.Readback.AuthoritativeRecoveryEpoch, Is.EqualTo("3"));
            Assert.That(result.Readback.CurrentIncarnation, Is.EqualTo("1"));
            Assert.That(result.Readback.CurrentActivationRequestId, Is.EqualTo(request.ActivationRequestId));
            Assert.That(Convert.FromBase64String(result.Readback.ActivationReceiptJsonBase64!),
                Is.EqualTo(activation.ImmutableReceiptJson));
        });
    }

    [Test]
    public async Task Stale_authority_epoch_reports_fenced_lifecycle_and_keeps_recovering_receipt()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var request = CreateInitialRequest(fixture);
        NodeActivationResult activation;
        await using (var activationDb = fixture.CreateContext())
            activation = await fixture.CreateActivationService(activationDb).ActivateAsync(
                Identity(fixture), request, Canonicalize(request), CancellationToken.None);

        await using var db = fixture.CreateContext();
        var authority = new FixedAuthority(4, "restore-revision-2");
        var result = await new NodeActivationReadbackService(db, authority)
            .ReadCurrentAsync(Identity(fixture), CancellationToken.None);

        var receipt = JsonSerializer.Deserialize<NodeActivationReceipt>(
            Convert.FromBase64String(result.Readback!.ActivationReceiptJsonBase64!), CanonicalOptions)!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Disposition, Is.EqualTo(NodeActivationReadbackDisposition.Success));
            Assert.That(result.Readback.Status, Is.EqualTo(NodeManagementStatuses.Fenced));
            Assert.That(result.Readback.RecoveryEpoch, Is.EqualTo("3"));
            Assert.That(result.Readback.AuthoritativeRecoveryEpoch, Is.EqualTo("4"));
            Assert.That(receipt.Status, Is.EqualTo(NodeManagementStatuses.Recovering));
            Assert.That(Convert.FromBase64String(result.Readback.ActivationReceiptJsonBase64!),
                Is.EqualTo(activation.ImmutableReceiptJson));
        });
    }

    [Test]
    public async Task Authority_change_while_readback_snapshot_is_open_returns_unavailable()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        await using var db = fixture.CreateContext();
        var result = await new NodeActivationReadbackService(db, new ChangingAuthority())
            .ReadCurrentAsync(Identity(fixture), CancellationToken.None);

        Assert.That(result.Disposition, Is.EqualTo(NodeActivationReadbackDisposition.Unavailable));
    }

    private static NodeIdentityResult Identity(NodeEnrollmentMySqlTestFixture fixture) =>
        NodeIdentityResult.Success(fixture.NodeId, fixture.InstallationId, fixture.RealmId);

    private static NodeActivationRequest CreateInitialRequest(NodeEnrollmentMySqlTestFixture fixture) => new(
        1, fixture.RealmId, 3, fixture.NodeId, fixture.InstallationId, Guid.NewGuid(), 0, 1,
        new string('1', 64), new string('2', 64), [1], null);

    private static byte[] Canonicalize(NodeActivationRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, CanonicalOptions);

    private sealed class FixedAuthority(long epoch, string revision) : INodeRecoveryEpochAuthority
    {
        public Task<bool> IsCurrentEpochAsync(Guid realmId, long recoveryEpoch, CancellationToken cancellationToken) =>
            Task.FromResult(realmId != Guid.Empty && recoveryEpoch == epoch);

        public Task<NodeRecoveryAuthoritySnapshot?> ReadCurrentAsync(Guid realmId, CancellationToken cancellationToken) =>
            Task.FromResult<NodeRecoveryAuthoritySnapshot?>(new NodeRecoveryAuthoritySnapshot(
                realmId, epoch, revision, new string('b', 64)));
    }

    private sealed class ChangingAuthority : INodeRecoveryEpochAuthority
    {
        private int readCount;

        public Task<bool> IsCurrentEpochAsync(Guid realmId, long recoveryEpoch, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<NodeRecoveryAuthoritySnapshot?> ReadCurrentAsync(Guid realmId, CancellationToken cancellationToken)
        {
            var revision = Interlocked.Increment(ref readCount);
            return Task.FromResult<NodeRecoveryAuthoritySnapshot?>(new NodeRecoveryAuthoritySnapshot(
                realmId, 3, $"revision-{revision}", new string('c', 64)));
        }
    }
}
