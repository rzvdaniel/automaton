using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationMySqlHostFenceTests
{
    [Test]
    public async Task Hfa_receipt_is_verified_and_consumed_with_restart_cas_once()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        var observation = await HostFenceTestSupport.CommitHfaRestartAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(observation.Result.Disposition, Is.EqualTo(NodeActivationDisposition.Accepted));
            Assert.That(observation.State.CurrentIncarnation, Is.EqualTo(2));
            Assert.That(observation.State.HostFenceGeneration, Is.EqualTo(1));
            Assert.That(observation.Uses, Is.EqualTo(1));
        });

        var retry = await HostFenceTestSupport.ActivateInOwnContextAsync(fixture, observation.Request, observation.Validator);
        Assert.That(retry.ImmutableReceiptJson, Is.EqualTo(observation.Result.ImmutableReceiptJson));
        Assert.That(await HostFenceTestSupport.CountFenceUsesAsync(fixture), Is.EqualTo(1));

        var replay = observation.Request with { ActivationRequestId = Guid.NewGuid() };
        await using var replayDb = fixture.CreateContext();
        var rejected = await fixture.CreateActivationService(replayDb, observation.Validator)
            .ActivateAsync(HostFenceTestSupport.Identity(fixture), replay,
                HostFenceTestSupport.Canonicalize(replay), CancellationToken.None);
        Assert.That(rejected.Disposition, Is.EqualTo(NodeActivationDisposition.Conflict));
        Assert.That(await HostFenceTestSupport.CountFenceUsesAsync(fixture), Is.EqualTo(1));
    }
}

[TestFixture]
[Category("MySqlIntegration")]
public sealed class NodeActivationEpochCasMySqlTests
{
    [Test]
    public async Task Epoch_change_after_authority_and_receipt_validation_makes_the_conditional_update_miss()
    {
        await using var fixture = await NodeEnrollmentMySqlTestFixture.CreateAsync();
        await fixture.CommitEnrollmentAsync();
        var initial = HostFenceTestSupport.CreateRequest(fixture, Guid.NewGuid(), 0);
        Assert.That((await HostFenceTestSupport.ActivateInOwnContextAsync(fixture, initial)).Disposition,
            Is.EqualTo(NodeActivationDisposition.Accepted));
        await HostFenceTestSupport.SetFencedStateAsync(fixture, initial.RecoveryCheckpointSha256);

        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = HostFenceTestSupport.CreateRequest(fixture, Guid.NewGuid(), 1);
        var payload = HostFenceTestSupport.CreateFencePayload(fixture, initial, request, 1);
        request = request with { HostFenceReceipt = NodeActivationContractTests.Sign(payload, signer) };
        var validator = new ChangeEpochAfterValidationValidator(
            new NodeHostFenceReceiptValidator(new MySqlHostFenceTestTrustStore(signer)), fixture);
        var result = await HostFenceTestSupport.ActivateInOwnContextAsync(fixture, request, validator);
        var state = await HostFenceTestSupport.LoadManagementAsync(fixture);
        var attempts = await HostFenceTestSupport.CountAttemptsAsync(fixture);

        Assert.Multiple(() =>
        {
            Assert.That(result.Disposition, Is.EqualTo(NodeActivationDisposition.Conflict));
            Assert.That(state.RecoveryEpoch, Is.EqualTo(4));
            Assert.That(state.CurrentIncarnation, Is.EqualTo(1));
            Assert.That(attempts, Is.EqualTo(1));
        });
    }

    private sealed class ChangeEpochAfterValidationValidator(INodeHostFenceReceiptValidator inner,
        NodeEnrollmentMySqlTestFixture fixture) : INodeHostFenceReceiptValidator
    {
        public bool IsConfigured => inner.IsConfigured;

        public bool TryValidate(NodeHostFenceReceiptEnvelope envelope, NodeActivationRequest request, long currentIncarnation,
            Guid priorActivationRequestId, long currentRecoveryEpoch, long lastHostFenceGeneration, DateTimeOffset now)
        {
            var valid = inner.TryValidate(envelope, request, currentIncarnation, priorActivationRequestId,
                currentRecoveryEpoch, lastHostFenceGeneration, now);
            if (valid)
            {
                using var db = fixture.CreateContext();
                db.NodeManagement.Where(value => value.NodeId == fixture.NodeId)
                    .ExecuteUpdateAsync(values => values.SetProperty(value => value.RecoveryEpoch, 4)).GetAwaiter().GetResult();
            }
            return valid;
        }
    }
}

internal static class HostFenceTestSupport
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<HfaRestartObservation> CommitHfaRestartAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await fixture.CommitEnrollmentAsync();
        var initial = CreateRequest(fixture, Guid.NewGuid(), 0);
        var first = await ActivateInOwnContextAsync(fixture, initial);
        Assert.That(first.Disposition, Is.EqualTo(NodeActivationDisposition.Accepted));
        await SetFencedStateAsync(fixture, initial.RecoveryCheckpointSha256);

        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var next = CreateRequest(fixture, Guid.NewGuid(), 1);
        var payload = CreateFencePayload(fixture, initial, next, 1);
        next = next with { HostFenceReceipt = NodeActivationContractTests.Sign(payload, signer) };
        var validator = new NodeHostFenceReceiptValidator(new MySqlHostFenceTestTrustStore(signer));
        var result = await ActivateInOwnContextAsync(fixture, next, validator);
        return new HfaRestartObservation(result, await LoadManagementAsync(fixture),
            await CountFenceUsesAsync(fixture), next, validator);
    }

    public static async Task<NodeActivationResult> ActivateInOwnContextAsync(NodeEnrollmentMySqlTestFixture fixture,
        NodeActivationRequest request, INodeHostFenceReceiptValidator? validator = null)
    {
        await using var db = fixture.CreateContext();
        return await fixture.CreateActivationService(db, validator)
            .ActivateAsync(Identity(fixture), request, Canonicalize(request), CancellationToken.None);
    }

    public static NodeActivationRequest CreateRequest(NodeEnrollmentMySqlTestFixture fixture, Guid requestId,
        long expectedIncarnation) => new(1, fixture.RealmId, 3, fixture.NodeId, fixture.InstallationId,
        requestId, expectedIncarnation, 1, new string('1', 64), new string('2', 64), [1], null);

    public static NodeIdentityResult Identity(NodeEnrollmentMySqlTestFixture fixture) =>
        NodeIdentityResult.Success(fixture.NodeId, fixture.InstallationId, fixture.RealmId);

    public static byte[] Canonicalize(NodeActivationRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, CanonicalOptions);

    public static NodeHostFenceReceiptPayload CreateFencePayload(NodeEnrollmentMySqlTestFixture fixture,
        NodeActivationRequest previous, NodeActivationRequest next, long generation)
    {
        var now = fixture.Now.ToUniversalTime();
        return new NodeHostFenceReceiptPayload(1, Guid.NewGuid(), "infra-ops", "mysql-lab",
            fixture.RealmId, fixture.NodeId, fixture.InstallationId, "host-wide", previous.RecoveryEpoch,
            previous.ExpectedIncarnation == 0 ? 1 : previous.ExpectedIncarnation, previous.ActivationRequestId,
            next.ActivationRequestId, previous.ExpectedIncarnation == 0 ? 1 : previous.ExpectedIncarnation,
            generation, now.AddSeconds(-2).ToString("O"), now.AddSeconds(-1).ToString("O"), now.AddMinutes(4).ToString("O"),
            new string('a', 64), new string('b', 64), previous.RecoveryCheckpointSha256);
    }

    public static async Task SetFencedStateAsync(NodeEnrollmentMySqlTestFixture fixture, string checkpoint)
    {
        await using var db = fixture.CreateContext();
        await db.NodeManagement.Where(value => value.NodeId == fixture.NodeId)
            .ExecuteUpdateAsync(values => values.SetProperty(value => value.Status, NodeManagementStatuses.Fenced)
                .SetProperty(value => value.RecoveryCheckpointSha256, checkpoint));
    }

    public static async Task<NodeManagement> LoadManagementAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using var db = fixture.CreateContext();
        return await db.NodeManagement.AsNoTracking().SingleAsync(value => value.NodeId == fixture.NodeId);
    }

    public static async Task<int> CountAttemptsAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using var db = fixture.CreateContext();
        return await db.NodeActivationAttempts.CountAsync();
    }

    public static async Task<int> CountFenceUsesAsync(NodeEnrollmentMySqlTestFixture fixture)
    {
        await using var db = fixture.CreateContext();
        return await db.NodeHostFenceReceiptUses.CountAsync();
    }

    private sealed class MySqlHostFenceTestTrustStore(ECDsa signer) : INodeHostFenceTrustStore
    {
        public bool HasTrustedKeys => true;

        public ECDsa? FindTrustedKey(string signerKeyId, string issuerId)
        {
            if (signerKeyId != "mysql-lab" || issuerId != "infra-ops") return null;
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(signer.ExportSubjectPublicKeyInfo(), out _);
            return key;
        }
    }

    internal sealed record HfaRestartObservation(NodeActivationResult Result, NodeManagement State,
        int Uses, NodeActivationRequest Request, INodeHostFenceReceiptValidator Validator);
}

internal sealed class MySqlHostFenceTestTrustStore(ECDsa signer) : INodeHostFenceTrustStore
{
    public bool HasTrustedKeys => true;

    public ECDsa? FindTrustedKey(string signerKeyId, string issuerId)
    {
        if (signerKeyId != "mysql-lab" || issuerId != "infra-ops") return null;
        var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(signer.ExportSubjectPublicKeyInfo(), out _);
        return key;
    }
}
