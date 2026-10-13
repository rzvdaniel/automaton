using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Automaton.Node.Preparation;

namespace Automaton.Node.Activation;

internal static class NodeActivationContractParser
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly Regex LowerSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static NodeActivationRequestDocument ParseRequest(byte[] bytes, NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority, NodeActivationIntent intent)
    {
        using var document = ParseUniqueObject(bytes, ["schemaVersion", "controlPlaneRealmId", "recoveryEpoch", "nodeId",
            "installationId", "activationRequestId", "expectedIncarnation", "credentialRevision", "journalInventorySha256",
            "recoveryCheckpointSha256", "supportedManagementSchemaVersions"], "hostFenceReceipt");
        var request = JsonSerializer.Deserialize<NodeActivationRequestDocument>(bytes, RequestOptions);
        if (request is null || !HasValidRequestFields(request, identity, authority, intent) ||
            !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(request, RequestOptions)))
            throw Failure();
        if (document.RootElement.TryGetProperty("hostFenceReceipt", out var hfa) && hfa.ValueKind == JsonValueKind.Null)
            throw Failure();
        if ((request.ExpectedIncarnation == 0 && request.HostFenceReceipt is not null) ||
            (request.ExpectedIncarnation > 0 && request.HostFenceReceipt is null))
            throw Failure();
        return request;
    }

    public static NodeActivationCurrentReadback ParseReadback(byte[] bytes)
    {
        using var document = ParseUniqueObject(bytes,
            ["schemaVersion", "controlPlaneRealmId", "nodeId", "installationId", "credentialRevision", "recoveryEpoch",
             "authoritativeRecoveryEpoch", "status", "currentIncarnation", "currentActivationRequestId", "requestSha256",
             "activationReceiptJsonBase64"]);
        var result = JsonSerializer.Deserialize<NodeActivationCurrentReadback>(bytes, ResponseOptions);
        if (result is null || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(result, ResponseOptions)) ||
            !HasValidReadbackHeader(result))
            throw Failure();
        ValidateReadbackShape(result);
        return result;
    }

    public static void ValidateReadbackBinding(NodeActivationCurrentReadback readback, NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority, IReadOnlyList<NodeActivationIntent> intents)
    {
        if (!IsReadbackBoundToAuthority(readback, identity, authority))
            throw Failure();
        if (readback.CurrentActivationRequestId is not Guid requestId)
            return;

        var intent = intents.SingleOrDefault(value => value.ActivationRequestId == requestId);
        if (intent is null || !IsIntentBoundToReadback(intent, readback, out var receiptBytes, out var requestBytes))
            throw Failure();

        var intentAuthority = authority with { RecoveryEpoch = ParsePositive(intent.RecoveryEpoch) };
        var request = ParseRequest(requestBytes, identity, intentAuthority, intent);
        _ = ParseReceipt(receiptBytes, identity, intentAuthority, intent, request);
        if (!IsStoredReceiptConsistent(intent, receiptBytes))
            throw Failure();
        if (readback.RecoveryEpoch != intent.RecoveryEpoch)
            throw Failure();
    }

    private static bool IsReadbackBoundToAuthority(NodeActivationCurrentReadback readback,
        NodeIdentityManifest identity, NodeExternalAuthorityEvidence authority) =>
        readback.ControlPlaneRealmId == identity.ControlPlaneRealmId && readback.NodeId == identity.NodeId &&
        readback.InstallationId == identity.InstallationId && readback.CredentialRevision == identity.CredentialRevision &&
        readback.AuthoritativeRecoveryEpoch == authority.RecoveryEpoch.ToString(CultureInfo.InvariantCulture);

    private static bool IsIntentBoundToReadback(NodeActivationIntent intent, NodeActivationCurrentReadback readback,
        out byte[] receiptBytes, out byte[] requestBytes)
    {
        receiptBytes = [];
        requestBytes = [];
        return intent.State is "ResponseUnknown" or "ReceiptPersisted" &&
            string.Equals(intent.RequestSha256, readback.RequestSha256, StringComparison.Ordinal) &&
            TryDecodeBase64(readback.ActivationReceiptJsonBase64!, out receiptBytes) &&
            TryDecodeBase64(intent.RequestJsonBase64, out requestBytes) && DigestMatches(requestBytes, intent.RequestSha256);
    }

    private static bool IsStoredReceiptConsistent(NodeActivationIntent intent, byte[] readbackReceipt) =>
        intent.State != "ReceiptPersisted" ||
        TryDecodeBase64(intent.ReceiptJsonBase64 ?? string.Empty, out var storedReceipt) &&
        storedReceipt.AsSpan().SequenceEqual(readbackReceipt);

    public static NodeActivationReceipt ParseReceipt(byte[] bytes, NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority, NodeActivationIntent intent, NodeActivationRequestDocument request)
    {
        using var document = ParseUniqueObject(bytes,
            ["schemaVersion", "nodeId", "installationId", "controlPlaneRealmId", "activationRequestId", "incarnation",
             "recoveryEpoch", "recoveryCheckpointSha256", "status", "createdAtUtc"]);
        var receipt = JsonSerializer.Deserialize<NodeActivationReceipt>(bytes, ResponseOptions);
        if (receipt is null || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(receipt, ResponseOptions)) ||
            !HasValidReceipt(receipt, identity, authority, intent, request))
            throw Failure();
        return receipt;
    }

    public static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = Convert.FromBase64String(value);
            return Convert.ToBase64String(bytes) == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool DigestMatches(byte[] bytes, string digest) =>
        LowerSha256.IsMatch(digest) && CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(digest));

    private static bool HasValidRequestFields(NodeActivationRequestDocument request, NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority, NodeActivationIntent intent) =>
        request.SchemaVersion == 1 && request.ControlPlaneRealmId == identity.ControlPlaneRealmId &&
        request.NodeId == identity.NodeId && request.InstallationId == identity.InstallationId &&
        request.ActivationRequestId == intent.ActivationRequestId && request.RecoveryEpoch == authority.RecoveryEpoch &&
        request.ExpectedIncarnation.ToString(CultureInfo.InvariantCulture) == intent.ExpectedIncarnation &&
        request.CredentialRevision.ToString(CultureInfo.InvariantCulture) == identity.CredentialRevision &&
        request.ExpectedIncarnation >= 0 && LowerSha256.IsMatch(request.JournalInventorySha256) &&
        LowerSha256.IsMatch(request.RecoveryCheckpointSha256) && HasValidSchemaVersions(request.SupportedManagementSchemaVersions);

    private static bool HasValidSchemaVersions(IReadOnlyList<int>? versions) =>
        versions is { Count: > 0 and <= 8 } && versions.All(value => value > 0) && versions.Distinct().Count() == versions.Count;

    private static bool HasValidReadbackHeader(NodeActivationCurrentReadback readback) =>
        readback.SchemaVersion == 1 && NodeIdentityManifest.TryPositive(readback.CredentialRevision, out _) &&
        NodeIdentityManifest.TryPositive(readback.AuthoritativeRecoveryEpoch, out _) &&
        Enum.TryParse<NodeReadbackStatus>(readback.Status, ignoreCase: false, out var status) && Enum.IsDefined(status);

    private static void ValidateReadbackShape(NodeActivationCurrentReadback readback)
    {
        if (readback.Status == "NoActivation")
        {
            if (readback.RecoveryEpoch is not null || HasAllocatedAttempt(readback)) throw Failure();
            return;
        }
        if (readback.Status == "Enrolled")
        {
            if (!NodeIdentityManifest.TryPositive(readback.RecoveryEpoch, out _) ||
                readback.RecoveryEpoch != readback.AuthoritativeRecoveryEpoch || HasAllocatedAttempt(readback)) throw Failure();
            return;
        }
        if (!HasPositiveCurrentAttempt(readback)) throw Failure();
        if ((readback.Status is "Recovering" or "Quiescing") &&
            readback.RecoveryEpoch != readback.AuthoritativeRecoveryEpoch)
            throw Failure();
    }

    private static bool HasAllocatedAttempt(NodeActivationCurrentReadback readback) =>
        readback.CurrentIncarnation is not null || readback.CurrentActivationRequestId is not null ||
        readback.RequestSha256 is not null || readback.ActivationReceiptJsonBase64 is not null;

    private static bool HasPositiveCurrentAttempt(NodeActivationCurrentReadback readback) =>
        NodeIdentityManifest.TryPositive(readback.RecoveryEpoch, out _) &&
        NodeIdentityManifest.TryPositive(readback.CurrentIncarnation, out _) &&
        readback.CurrentActivationRequestId is not null && LowerSha256.IsMatch(readback.RequestSha256 ?? string.Empty) &&
        TryDecodeBase64(readback.ActivationReceiptJsonBase64 ?? string.Empty, out var bytes) && bytes.Length <= MaximumBytes;

    private static bool HasValidReceipt(NodeActivationReceipt receipt, NodeIdentityManifest identity,
        NodeExternalAuthorityEvidence authority, NodeActivationIntent intent, NodeActivationRequestDocument request) =>
        receipt.SchemaVersion == 1 && receipt.NodeId == identity.NodeId && receipt.InstallationId == identity.InstallationId &&
        receipt.ControlPlaneRealmId == identity.ControlPlaneRealmId && receipt.ActivationRequestId == intent.ActivationRequestId &&
        receipt.RecoveryEpoch == authority.RecoveryEpoch && receipt.RecoveryCheckpointSha256 == request.RecoveryCheckpointSha256 &&
        receipt.Status == "Recovering" && request.ExpectedIncarnation < long.MaxValue &&
        receipt.Incarnation == request.ExpectedIncarnation + 1 && receipt.CreatedAtUtc != default &&
        receipt.CreatedAtUtc.Kind == DateTimeKind.Utc;

    private static long ParsePositive(string value) =>
        NodeIdentityManifest.TryPositive(value, out var parsed) ? parsed : throw Failure();

    private static JsonDocument ParseUniqueObject(byte[] bytes, string[] required, string? optional = null)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Failure();
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException) { throw Failure(); }
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw Failure();
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!document.RootElement.EnumerateObject().All(property => names.Add(property.Name)))
        {
            document.Dispose();
            throw Failure();
        }
        if (!required.All(names.Contains) || names.Except(required, StringComparer.Ordinal).Any(name => name != optional))
        {
            document.Dispose();
            throw Failure();
        }
        return document;
    }

    private static InvalidDataException Failure() => new("Node activation contract is invalid.");

    private enum NodeReadbackStatus { NoActivation, Enrolled, Recovering, Quiescing, Fenced }
}
