using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Automaton.Api.Services;

public sealed record NodeActivationRequest(
    int SchemaVersion,
    Guid ControlPlaneRealmId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RecoveryEpoch,
    Guid NodeId,
    Guid InstallationId,
    Guid ActivationRequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedIncarnation,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long CredentialRevision,
    string JournalInventorySha256,
    string RecoveryCheckpointSha256,
    IReadOnlyList<int> SupportedManagementSchemaVersions,
    NodeHostFenceReceiptEnvelope? HostFenceReceipt);

public sealed record NodeHostFenceReceiptEnvelope(NodeHostFenceReceiptPayload Payload, string SignatureAlgorithm, string Signature);

public sealed record NodeHostFenceReceiptPayload(
    int SchemaVersion,
    Guid ReceiptId,
    string IssuerId,
    string SignerKeyId,
    Guid ControlPlaneRealmId,
    Guid NodeId,
    Guid InstallationId,
    string Scope,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long PriorRecoveryEpoch,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long PriorIncarnation,
    Guid PriorActivationRequestId,
    Guid NextActivationRequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long ExpectedCurrentIncarnation,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long HostFenceGeneration,
    string FencedAtUtc,
    string IssuedAtUtc,
    string ExpiresAtUtc,
    string FenceEvidenceSha256,
    string EffectDispositionSha256,
    string RecoveryCheckpointSha256);

public sealed record NodeActivationReceipt(
    int SchemaVersion,
    Guid NodeId,
    Guid InstallationId,
    Guid ControlPlaneRealmId,
    Guid ActivationRequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Incarnation,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RecoveryEpoch,
    string RecoveryCheckpointSha256,
    string Status,
    DateTime CreatedAtUtc);

public enum NodeActivationDisposition { Accepted, Conflict, Invalid, Unavailable }
public sealed record NodeActivationResult(NodeActivationDisposition Disposition, string? ErrorCode, byte[]? ImmutableReceiptJson)
{
    public static NodeActivationResult Accepted(byte[] receipt) => new(NodeActivationDisposition.Accepted, null, receipt);
    public static NodeActivationResult Conflict(string code) => new(NodeActivationDisposition.Conflict, code, null);
    public static NodeActivationResult Invalid => new(NodeActivationDisposition.Invalid, "invalid_activation_request", null);
    public static NodeActivationResult Unavailable => new(NodeActivationDisposition.Unavailable, "activation_authority_unavailable", null);
}

internal static class NodeActivationRequestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryParse(ReadOnlyMemory<byte> json, out NodeActivationRequest? request, out byte[] canonicalBytes)
    {
        request = null;
        canonicalBytes = Array.Empty<byte>();
        if (json.Length is 0 or > 64 * 1024)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            if (!HasExactUniqueProperties(document.RootElement,
                    ["schemaVersion", "controlPlaneRealmId", "recoveryEpoch", "nodeId", "installationId",
                     "activationRequestId", "expectedIncarnation", "credentialRevision", "journalInventorySha256",
                     "recoveryCheckpointSha256", "supportedManagementSchemaVersions"], "hostFenceReceipt"))
                return false;
            request = JsonSerializer.Deserialize<NodeActivationRequest>(json.Span, JsonOptions);
            if (request is null || !IsValidRequest(request, document.RootElement))
                return false;

            canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            return json.Span.SequenceEqual(canonicalBytes);
        }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static bool IsValidRequest(NodeActivationRequest request, JsonElement root) =>
        HasValidRequestValues(request) && HasCanonicalRequestValues(request, root) && IsValidFenceEnvelope(request, root);

    private static bool HasValidRequestValues(NodeActivationRequest request) =>
        request.SchemaVersion == 1 && request.RecoveryEpoch > 0 && request.ExpectedIncarnation >= 0 &&
        request.ControlPlaneRealmId != Guid.Empty && request.NodeId != Guid.Empty && request.InstallationId != Guid.Empty &&
        request.ActivationRequestId != Guid.Empty && request.CredentialRevision > 0 &&
        IsDigest(request.JournalInventorySha256) && IsDigest(request.RecoveryCheckpointSha256) &&
        IsValidSchemaVersions(request.SupportedManagementSchemaVersions);

    private static bool HasCanonicalRequestValues(NodeActivationRequest request, JsonElement root) =>
        HasCanonicalGuid(root, "controlPlaneRealmId", request.ControlPlaneRealmId) &&
        HasCanonicalGuid(root, "nodeId", request.NodeId) && HasCanonicalGuid(root, "installationId", request.InstallationId) &&
        HasCanonicalGuid(root, "activationRequestId", request.ActivationRequestId) &&
        HasCanonicalPositiveInteger(root, "recoveryEpoch", request.RecoveryEpoch) &&
        HasCanonicalExpectedInteger(root, request.ExpectedIncarnation) &&
        HasCanonicalPositiveInteger(root, "credentialRevision", request.CredentialRevision);

    private static bool IsValidSchemaVersions(IReadOnlyList<int>? versions) =>
        versions is not null && versions.Count is > 0 and <= 16 && versions.All(value => value > 0) &&
        versions.Distinct().Count() == versions.Count;

    private static bool IsValidFenceEnvelope(NodeActivationRequest request, JsonElement root)
    {
        if (request.HostFenceReceipt is null)
            return !root.TryGetProperty("hostFenceReceipt", out _);
        if (!root.TryGetProperty("hostFenceReceipt", out var envelope) ||
            !HasExactUniqueProperties(envelope, ["payload", "signatureAlgorithm", "signature"]) ||
            !envelope.TryGetProperty("payload", out var payload))
            return false;
        return HasExactUniqueProperties(payload,
            "schemaVersion", "receiptId", "issuerId", "signerKeyId", "controlPlaneRealmId", "nodeId",
            "installationId", "scope", "priorRecoveryEpoch", "priorIncarnation", "priorActivationRequestId",
            "nextActivationRequestId", "expectedCurrentIncarnation", "hostFenceGeneration", "fencedAtUtc",
            "issuedAtUtc", "expiresAtUtc", "fenceEvidenceSha256", "effectDispositionSha256", "recoveryCheckpointSha256");
    }

    internal static bool IsDigest(string? value) => value is not null && DigestPattern.IsMatch(value);

    internal static bool HasExactUniqueProperties(JsonElement element, params string[] expected) =>
        HasExactUniqueProperties(element, expected, Array.Empty<string>());

    private static bool HasExactUniqueProperties(JsonElement element, string[] required, params string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) return false;
        return required.All(names.Contains) && names.All(name => required.Contains(name, StringComparer.Ordinal) || optional.Contains(name, StringComparer.Ordinal));
    }

    private static bool HasCanonicalGuid(JsonElement root, string name, Guid parsed) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString() == parsed.ToString("D", CultureInfo.InvariantCulture);

    private static bool HasCanonicalPositiveInteger(JsonElement root, string name, long value) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString() == value.ToString(CultureInfo.InvariantCulture);

    private static bool HasCanonicalExpectedInteger(JsonElement root, long value) =>
        root.TryGetProperty("expectedIncarnation", out var property) && property.ValueKind == JsonValueKind.String &&
        property.GetString() == value.ToString(CultureInfo.InvariantCulture);
}

public interface INodeRecoveryEpochAuthority
{
    Task<bool> IsCurrentEpochAsync(Guid realmId, long recoveryEpoch, CancellationToken cancellationToken);
}

/// <summary>Production default: activation remains disabled until an independently reviewed register adapter is configured.</summary>
internal sealed class UnavailableNodeRecoveryEpochAuthority : INodeRecoveryEpochAuthority
{
    public Task<bool> IsCurrentEpochAsync(Guid realmId, long recoveryEpoch, CancellationToken cancellationToken) => Task.FromResult(false);
}

public interface INodeHostFenceTrustStore
{
    bool HasTrustedKeys { get; }
    ECDsa? FindTrustedKey(string signerKeyId, string issuerId);
}

public sealed class NodeHostFenceTrustKeyOptions
{
    public string IssuerId { get; set; } = string.Empty;
    public string SignerKeyId { get; set; } = string.Empty;
    public string PublicKeySpkiBase64 { get; set; } = string.Empty;
}

internal sealed class ConfiguredNodeHostFenceTrustStore(IConfiguration configuration) : INodeHostFenceTrustStore
{
    public bool HasTrustedKeys
    {
        get
        {
            var entries = configuration.GetSection("NodeActivation:HfaTrustKeys").Get<NodeHostFenceTrustKeyOptions[]>();
            return entries?.Any(entry =>
            {
                using var key = FindTrustedKey(entry.SignerKeyId, entry.IssuerId);
                return key is not null;
            }) == true;
        }
    }

    public ECDsa? FindTrustedKey(string signerKeyId, string issuerId)
    {
        var entries = configuration.GetSection("NodeActivation:HfaTrustKeys").Get<NodeHostFenceTrustKeyOptions[]>();
        var entry = entries?.SingleOrDefault(value => value.SignerKeyId == signerKeyId && value.IssuerId == issuerId);
        if (entry is null || string.IsNullOrWhiteSpace(entry.PublicKeySpkiBase64) || entry.PublicKeySpkiBase64.Length > 4096)
            return null;
        try
        {
            var encoded = Convert.FromBase64String(entry.PublicKeySpkiBase64);
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(encoded, out var read);
            if (read != encoded.Length || key.KeySize != 256)
            {
                key.Dispose();
                return null;
            }
            return key;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }
    }
}

public interface INodeHostFenceReceiptValidator
{
    bool IsConfigured { get; }
    bool TryValidate(
        NodeHostFenceReceiptEnvelope envelope,
        NodeActivationRequest request,
        long currentIncarnation,
        Guid priorActivationRequestId,
        long currentRecoveryEpoch,
        long lastHostFenceGeneration,
        DateTimeOffset now);
}

/// <summary>Shared production/test validator; the trust-store implementation determines the trusted key set.</summary>
public sealed class NodeHostFenceReceiptValidator(INodeHostFenceTrustStore trustStore) : INodeHostFenceReceiptValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("automaton.host-fence-receipt.v1\n");

    public bool IsConfigured => trustStore.HasTrustedKeys;

    public bool TryValidate(
        NodeHostFenceReceiptEnvelope envelope,
        NodeActivationRequest request,
        long currentIncarnation,
        Guid priorActivationRequestId,
        long currentRecoveryEpoch,
        long lastHostFenceGeneration,
        DateTimeOffset now)
    {
        var payload = envelope.Payload;
        if (!HasValidBindings(payload, request, currentIncarnation, priorActivationRequestId, currentRecoveryEpoch, lastHostFenceGeneration) ||
            !HasValidWindow(payload, now) || !TryDecodeSignature(envelope.Signature, out var signature))
            return false;

        var key = trustStore.FindTrustedKey(payload.SignerKeyId, payload.IssuerId);
        if (key is null)
            return false;
        try
        {
            var canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            var data = new byte[Domain.Length + canonicalPayload.Length];
            Domain.CopyTo(data, 0);
            canonicalPayload.CopyTo(data, Domain.Length);
            return key.KeySize == 256 && key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        finally { key.Dispose(); }
    }

    private static bool HasValidBindings(NodeHostFenceReceiptPayload payload, NodeActivationRequest request,
        long currentIncarnation, Guid priorRequestId, long currentEpoch, long lastGeneration) =>
        HasValidReceiptIdentity(payload, request) && HasValidPriorFence(payload, currentIncarnation, priorRequestId, currentEpoch) &&
        HasValidCheckpointAndGeneration(payload, request, lastGeneration);

    private static bool HasValidReceiptIdentity(NodeHostFenceReceiptPayload payload, NodeActivationRequest request) =>
        payload.SchemaVersion == 1 && payload.Scope == "host-wide" &&
        payload.ReceiptId != Guid.Empty && IsBoundedIdentity(payload.IssuerId) && IsBoundedIdentity(payload.SignerKeyId) &&
        NodeActivationRequestParser.IsDigest(payload.FenceEvidenceSha256) &&
        NodeActivationRequestParser.IsDigest(payload.EffectDispositionSha256) &&
        NodeActivationRequestParser.IsDigest(payload.RecoveryCheckpointSha256) && payload.ControlPlaneRealmId == request.ControlPlaneRealmId &&
        payload.NodeId == request.NodeId && payload.InstallationId == request.InstallationId;

    private static bool HasValidPriorFence(NodeHostFenceReceiptPayload payload, long currentIncarnation,
        Guid priorRequestId, long currentEpoch) => payload.PriorRecoveryEpoch == currentEpoch &&
        payload.PriorIncarnation == currentIncarnation && payload.ExpectedCurrentIncarnation == currentIncarnation &&
        payload.PriorActivationRequestId == priorRequestId;

    private static bool HasValidCheckpointAndGeneration(NodeHostFenceReceiptPayload payload,
        NodeActivationRequest request, long lastGeneration) => payload.NextActivationRequestId == request.ActivationRequestId &&
        payload.RecoveryCheckpointSha256 == request.RecoveryCheckpointSha256 && payload.HostFenceGeneration > lastGeneration;

    private static bool IsBoundedIdentity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        !value.Any(char.IsControl);

    private static bool HasValidWindow(NodeHostFenceReceiptPayload payload, DateTimeOffset now)
    {
        if (!TryTimestamp(payload.FencedAtUtc, out var fencedAt) || !TryTimestamp(payload.IssuedAtUtc, out var issuedAt) ||
            !TryTimestamp(payload.ExpiresAtUtc, out var expiresAt)) return false;
        return issuedAt <= now.AddMinutes(1) && fencedAt <= issuedAt && expiresAt > now && expiresAt > issuedAt &&
               expiresAt - issuedAt <= TimeSpan.FromMinutes(10);
    }

    private static bool TryTimestamp(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result) &&
        result.Offset == TimeSpan.Zero;

    private static bool TryDecodeSignature(string value, out byte[] signature)
    {
        signature = Array.Empty<byte>();
        if (value.Length > 128) return false;
        try
        {
            signature = Convert.FromBase64String(value);
            return signature.Length == 64 && Convert.ToBase64String(signature) == value;
        }
        catch (FormatException) { return false; }
    }
}
