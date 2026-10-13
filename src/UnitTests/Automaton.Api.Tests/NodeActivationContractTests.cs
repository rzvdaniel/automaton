using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class NodeActivationContractTests
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Test]
    public void Initial_activation_accepts_only_the_canonical_zero_sentinel()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(CreateRequest(expectedIncarnation: 0), CanonicalOptions);

        Assert.Multiple(() =>
        {
            Assert.That(NodeActivationRequestParser.TryParse(bytes, out var parsed, out var canonical), Is.True);
            Assert.That(parsed!.ExpectedIncarnation, Is.Zero);
            Assert.That(canonical, Is.EqualTo(bytes));
            Assert.That(NodeActivationRequestParser.TryParse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"0\"", "0")), out _, out _), Is.False);
            Assert.That(NodeActivationRequestParser.TryParse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"expectedIncarnation\":\"0\"", "\"expectedIncarnation\":\"00\"")), out _, out _), Is.False);
        });
    }

    [TestCase("unknownProperty")]
    [TestCase("hostFenceReceipt")]
    public void Strict_parser_rejects_unknown_or_explicit_null_optional_fields(string field)
    {
        var bytes = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(CreateRequest(0), CanonicalOptions));
        var changed = field == "hostFenceReceipt"
            ? bytes.Insert(bytes.Length - 1, ",\"hostFenceReceipt\":null")
            : bytes.Insert(bytes.Length - 1, ",\"unknownProperty\":1");

        Assert.That(NodeActivationRequestParser.TryParse(Encoding.UTF8.GetBytes(changed), out _, out _), Is.False);
    }

    [Test]
    public void Strict_parser_rejects_duplicate_keys_and_noncanonical_json()
    {
        var valid = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(CreateRequest(0), CanonicalOptions));
        var duplicate = valid.Insert(1, "\"schemaVersion\":1,");
        var whitespace = valid.Replace(",\"controlPlaneRealmId\"", ", \"controlPlaneRealmId\"");

        Assert.Multiple(() =>
        {
            Assert.That(NodeActivationRequestParser.TryParse(Encoding.UTF8.GetBytes(duplicate), out _, out _), Is.False);
            Assert.That(NodeActivationRequestParser.TryParse(Encoding.UTF8.GetBytes(whitespace), out _, out _), Is.False);
        });
    }

    [Test]
    public void Host_fence_validator_checks_signature_and_every_restart_binding()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var request = CreateRequest(4) with { HostFenceReceipt = null };
        var payload = new NodeHostFenceReceiptPayload(1, Guid.NewGuid(), "infra-ops", "lab-key",
            request.ControlPlaneRealmId, request.NodeId, request.InstallationId, "host-wide", request.RecoveryEpoch,
            4, Guid.NewGuid(), request.ActivationRequestId, 4, 9,
            now.AddSeconds(-3).ToString("O"), now.AddSeconds(-2).ToString("O"), now.AddMinutes(3).ToString("O"),
            new string('a', 64), new string('b', 64), request.RecoveryCheckpointSha256);
        request = request with { HostFenceReceipt = Sign(payload, signer) };
        var validator = new NodeHostFenceReceiptValidator(new TestTrustStore(signer));

        Assert.Multiple(() =>
        {
            Assert.That(validator.TryValidate(request.HostFenceReceipt!, request, 4, payload.PriorActivationRequestId,
                request.RecoveryEpoch, 8, now), Is.True);
            Assert.That(validator.TryValidate(request.HostFenceReceipt!, request, 5, payload.PriorActivationRequestId,
                request.RecoveryEpoch, 8, now), Is.False);
            Assert.That(validator.TryValidate(request.HostFenceReceipt!, request with { RecoveryCheckpointSha256 = new string('c', 64) },
                4, payload.PriorActivationRequestId, request.RecoveryEpoch, 8, now), Is.False);
            Assert.That(validator.TryValidate(request.HostFenceReceipt!, request, 4, payload.PriorActivationRequestId,
                request.RecoveryEpoch + 1, 8, now), Is.False);
        });
    }

    [Test]
    public async Task Production_epoch_authority_fails_closed_without_external_register_adapter()
    {
        var authority = new UnavailableNodeRecoveryEpochAuthority();
        var authorityAccepts = await authority.IsCurrentEpochAsync(Guid.NewGuid(), 1, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(authorityAccepts, Is.False);
            Assert.That(new NodeHostFenceReceiptValidator(new EmptyHostFenceTrustStore()).IsConfigured, Is.False);
        });
    }

    internal static NodeActivationRequest CreateRequest(long expectedIncarnation) => new(
        1, Guid.Parse("40000000-0000-0000-0000-000000000001"), 3,
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("60000000-0000-0000-0000-000000000001"),
        Guid.Parse("70000000-0000-0000-0000-000000000001"), expectedIncarnation, 1,
        new string('1', 64), new string('2', 64), [1], null);

    internal static NodeHostFenceReceiptEnvelope Sign(NodeHostFenceReceiptPayload payload, ECDsa signer)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalOptions);
        var prefix = Encoding.UTF8.GetBytes("automaton.host-fence-receipt.v1\n");
        return new NodeHostFenceReceiptEnvelope(payload, "ECDSA_P256_SHA256_P1363",
            Convert.ToBase64String(signer.SignData(prefix.Concat(bytes).ToArray(), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }

    private sealed class TestTrustStore(ECDsa signer) : INodeHostFenceTrustStore
    {
        public bool HasTrustedKeys => true;

        public ECDsa? FindTrustedKey(string signerKeyId, string issuerId)
        {
            if (signerKeyId != "lab-key" || issuerId != "infra-ops") return null;
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(signer.ExportSubjectPublicKeyInfo(), out _);
            return key;
        }
    }

    private sealed class EmptyHostFenceTrustStore : INodeHostFenceTrustStore
    {
        public bool HasTrustedKeys => false;
        public ECDsa? FindTrustedKey(string signerKeyId, string issuerId) => null;
    }
}
