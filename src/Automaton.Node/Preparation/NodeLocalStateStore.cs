using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Node.Activation;

namespace Automaton.Node.Preparation;

internal sealed class NodeLocalStateStore(NodeInstallationLock installationLock) : INodeActivationIntentLedger
{
    private const int MaximumIdentityBytes = 16 * 1024;
    private const int MaximumCheckpointBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public NodeIdentityManifest? ReadIdentity()
    {
        var bytes = GetDirectory().Read("node-identity.json", MaximumIdentityBytes);
        return bytes is null ? null : NodeIdentityManifest.Parse(bytes);
    }

    public NodeActivationCheckpoint? ReadCheckpoint()
    {
        var bytes = GetDirectory().Read("activation-checkpoint.json", MaximumCheckpointBytes);
        return bytes is null ? null : NodeActivationCheckpoint.Parse(bytes);
    }

    public void WriteCheckpoint(NodeActivationCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var identity = ReadIdentity() ?? throw new NodeProtectedStateException();
        ValidateInstalledCredentials(identity);
        if (!checkpoint.IsBoundTo(identity))
            throw new NodeProtectedStateException();
        var bytes = checkpoint.SerializeAndValidate();
        GetDirectory().AtomicReplace("activation-checkpoint.json", bytes);
    }

    public NodeLocalClientCredentials LoadClientCredentials()
    {
        var identity = ReadIdentity() ?? throw new NodeProtectedStateException();
        ValidateInstalledCredentials(identity);
        var directory = GetDirectory();
        var certificateBytes = directory.Read("node-client-cert.der", 32 * 1024)!;
        var privateKeyBytes = directory.Read("node-client-key.pem", 32 * 1024)!;
        var apiKeyBytes = directory.Read("node-api-key", 256)!;
        try
        {
            using var publicCertificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            var certificate = X509Certificate2.CreateFromPem(publicCertificate.ExportCertificatePem(),
                Encoding.ASCII.GetString(privateKeyBytes));
            return new NodeLocalClientCredentials(identity, certificate, Encoding.ASCII.GetString(apiKeyBytes));
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new NodeProtectedStateException();
        }
    }

    public NodeActivationIntent PrepareIntent(Guid processStartId, Guid requestId, long expectedIncarnation,
        long recoveryEpoch, ReadOnlySpan<byte> canonicalRequest)
    {
        var snapshot = ReadSnapshot();
        var identity = snapshot.Identity ?? throw new NodeProtectedStateException();
        var checkpoint = snapshot.Checkpoint ?? throw new NodeProtectedStateException();
        if (!CanPrepareIntent(checkpoint, identity, processStartId, requestId, expectedIncarnation, recoveryEpoch, canonicalRequest))
            throw new NodeProtectedStateException();

        var intents = checkpoint.ActivationIntents
            .Select(value => value.State == "Prepared" && value.ProcessStartId != processStartId
                ? value with { State = "AbandonedUnsent" }
                : value)
            .ToList();
        if (intents.Count >= 256)
            intents = CompactSettled(intents, checkpoint.LastActivationRequestId);
        if (intents.Count >= 256)
            throw new NodeProtectedStateException();

        var requestBytes = canonicalRequest.ToArray();
        var intent = new NodeActivationIntent(requestId, processStartId,
            expectedIncarnation.ToString(CultureInfo.InvariantCulture), recoveryEpoch.ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant(), Convert.ToBase64String(requestBytes),
            "Prepared", null, null);
        intents.Add(intent);
        WriteCheckpoint(checkpoint.Advance(intents, requestId, checkpoint.LastKnownRecoveryEpoch,
            checkpoint.CurrentIncarnation, checkpoint.LastActivationRequestId,
            checkpoint.LastActivationReceiptJsonBase64, checkpoint.LastActivationReceiptSha256));
        return intent;
    }

    public NodeActivationIntent MarkResponseUnknown(Guid requestId)
    {
        var checkpoint = ReadSnapshot().Checkpoint ?? throw new NodeProtectedStateException();
        var intent = checkpoint.ActivationIntents.SingleOrDefault(value => value.ActivationRequestId == requestId);
        if (intent is null || intent.State != "Prepared" || checkpoint.CurrentProcessIntentId != requestId)
            throw new NodeProtectedStateException();
        var updated = intent with { State = "ResponseUnknown" };
        WriteCheckpoint(checkpoint.Advance(ReplaceIntent(checkpoint.ActivationIntents, updated), requestId,
            checkpoint.LastKnownRecoveryEpoch, checkpoint.CurrentIncarnation, checkpoint.LastActivationRequestId,
            checkpoint.LastActivationReceiptJsonBase64, checkpoint.LastActivationReceiptSha256));
        return updated;
    }

    public NodeActivationIntent GetIntent(Guid requestId)
    {
        var checkpoint = ReadSnapshot().Checkpoint ?? throw new NodeProtectedStateException();
        return checkpoint.ActivationIntents.SingleOrDefault(value => value.ActivationRequestId == requestId)
            ?? throw new NodeProtectedStateException();
    }

    public NodeActivationIntent PersistReceipt(Guid requestId, NodeActivationReceipt receipt, ReadOnlySpan<byte> exactReceipt)
    {
        var checkpoint = ReadSnapshot().Checkpoint ?? throw new NodeProtectedStateException();
        var intent = checkpoint.ActivationIntents.SingleOrDefault(value => value.ActivationRequestId == requestId);
        if (intent is null || !IsPersistableReceipt(checkpoint, intent, requestId, receipt, exactReceipt, out var epoch))
            throw new NodeProtectedStateException();

        var receiptBytes = exactReceipt.ToArray();
        var receiptSha256 = Convert.ToHexString(SHA256.HashData(receiptBytes)).ToLowerInvariant();
        var updated = intent with
        {
            State = "ReceiptPersisted",
            ReceiptJsonBase64 = Convert.ToBase64String(receiptBytes),
            ReceiptSha256 = receiptSha256
        };
        WriteCheckpoint(checkpoint.Advance(ReplaceIntent(checkpoint.ActivationIntents, updated), null,
            epoch.ToString(CultureInfo.InvariantCulture), receipt.Incarnation.ToString(CultureInfo.InvariantCulture),
            requestId, updated.ReceiptJsonBase64, receiptSha256));
        return updated;
    }

    public void MarkSamePreconditionLosersFencedOut(NodeActivationCurrentReadback winningReadback)
    {
        var checkpoint = ReadSnapshot().Checkpoint ?? throw new NodeProtectedStateException();
        if (winningReadback.CurrentActivationRequestId is not Guid winnerId ||
            !NodeIdentityManifest.TryPositive(winningReadback.CurrentIncarnation, out var winningIncarnation) ||
            !NodeIdentityManifest.TryPositive(winningReadback.RecoveryEpoch, out var winningEpoch))
            throw new NodeProtectedStateException();
        var winner = checkpoint.ActivationIntents.SingleOrDefault(value => value.ActivationRequestId == winnerId);
        if (winner is null || !IsValidWinningIntent(checkpoint, winningReadback, winner, winningIncarnation, winningEpoch))
            throw new NodeProtectedStateException();

        var intents = checkpoint.ActivationIntents.Select(value =>
        {
            if (value.ActivationRequestId == winnerId || value.State != "ResponseUnknown" ||
                value.ExpectedIncarnation != winner.ExpectedIncarnation || value.RecoveryEpoch != winner.RecoveryEpoch)
                return value;
            return value with { State = "FencedOut" };
        }).ToArray();
        WriteCheckpoint(checkpoint.Advance(intents, null, checkpoint.LastKnownRecoveryEpoch,
            checkpoint.CurrentIncarnation, checkpoint.LastActivationRequestId,
            checkpoint.LastActivationReceiptJsonBase64, checkpoint.LastActivationReceiptSha256));
    }

    public NodeIdentityManifest InstallVerifiedBundle(NodeVerifiedCredentialBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var manifest = ValidateBundle(bundle);
        var directory = GetDirectory();
        var existingManifest = directory.Read("node-identity.json", MaximumIdentityBytes);
        if (existingManifest is not null)
        {
            var existing = NodeIdentityManifest.Parse(existingManifest);
            if (!existingManifest.AsSpan().SequenceEqual(manifest.Serialize()) ||
                !BytesEqual(directory.Read("node-client-cert.der", 32 * 1024), bundle.CertificateDer) ||
                !BytesEqual(directory.Read("node-client-key.pem", 32 * 1024), Encoding.ASCII.GetBytes(bundle.PrivateKeyPem)) ||
                !BytesEqual(directory.Read("node-api-key", 256), Encoding.ASCII.GetBytes(bundle.ApiKey)))
                throw new NodeProtectedStateException();
            return existing;
        }

        if (directory.Read("node-client-cert.der", 32 * 1024) is not null ||
            directory.Read("node-client-key.pem", 32 * 1024) is not null ||
            directory.Read("node-api-key", 256) is not null)
            throw new NodeProtectedStateException();

        directory.AtomicReplace("node-client-key.pem", Encoding.ASCII.GetBytes(bundle.PrivateKeyPem));
        directory.AtomicReplace("node-client-cert.der", bundle.CertificateDer);
        directory.AtomicReplace("node-api-key", Encoding.ASCII.GetBytes(bundle.ApiKey));
        directory.AtomicReplace("node-identity.json", manifest.Serialize());
        return manifest;
    }

    public NodeLocalStateSnapshot ReadSnapshot()
    {
        var identity = ReadIdentity();
        var checkpoint = ReadCheckpoint();
        if (identity is null && checkpoint is not null)
            throw new NodeProtectedStateException();
        if (identity is not null)
            ValidateInstalledCredentials(identity);
        if (checkpoint is not null && !checkpoint.IsBoundTo(identity!))
            throw new NodeProtectedStateException();
        return new NodeLocalStateSnapshot(identity, checkpoint);
    }

    public NodeActivationCheckpoint InitializeGenesis(NodeActivationReadbackSnapshot readback,
        NodeExternalAuthorityEvidence authority)
    {
        ArgumentNullException.ThrowIfNull(readback);
        ArgumentNullException.ThrowIfNull(authority);
        var identity = ReadIdentity() ?? throw new NodeProtectedStateException();
        ValidateInstalledCredentials(identity);
        if (!IsValidGenesisEvidence(identity, readback, authority) || ReadCheckpoint() is not null)
            throw new NodeProtectedStateException();

        var emptyInventory = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"entries\":[]}");
        var emptyDigest = Convert.ToHexString(SHA256.HashData(emptyInventory)).ToLowerInvariant();
        var evidence = new NodeFreshEnrollmentGenesisEvidence(1, identity.EnrollmentReceiptSha256,
            authority.AttestationSha256, readback.ResponseSha256);
        var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(evidence, CanonicalJsonOptions);
        var evidenceDigest = Convert.ToHexString(SHA256.HashData(evidenceBytes)).ToLowerInvariant();
        var recoveryView = NodeRecoveryView.Create(identity, authority.RecoveryEpoch, null, emptyDigest, emptyDigest,
            evidenceDigest);
        var checkpoint = NodeActivationCheckpoint.CreateGenesis(identity, authority.RecoveryEpoch, recoveryView);
        WriteCheckpoint(checkpoint);
        return checkpoint;
    }

    private NodeProtectedStateDirectory GetDirectory() =>
        installationLock.StateDirectory ?? throw new NodeProtectedStateException();

    private void ValidateInstalledCredentials(NodeIdentityManifest identity)
    {
        var directory = GetDirectory();
        var certificateBytes = directory.Read("node-client-cert.der", 32 * 1024);
        var privateKeyBytes = directory.Read("node-client-key.pem", 32 * 1024);
        var apiKeyBytes = directory.Read("node-api-key", 256);
        if (certificateBytes is null || privateKeyBytes is null || apiKeyBytes is null || apiKeyBytes.Length is 0 or > 256 ||
            apiKeyBytes.Any(value => value is < 0x21 or > 0x7E) ||
            Convert.ToHexString(SHA256.HashData(certificateBytes)).ToLowerInvariant() != identity.NodeCertificateDerSha256)
            throw new NodeProtectedStateException();

        try
        {
            using var certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            using var publicKey = certificate.GetECDsaPublicKey();
            using var privateKey = ECDsa.Create();
            privateKey.ImportFromPem(Encoding.ASCII.GetString(privateKeyBytes));
            if (publicKey is null || publicKey.KeySize != 256 || privateKey.KeySize != 256 ||
                !CryptographicOperations.FixedTimeEquals(publicKey.ExportSubjectPublicKeyInfo(), privateKey.ExportSubjectPublicKeyInfo()))
                throw new NodeProtectedStateException();
        }
        catch (CryptographicException)
        {
            throw new NodeProtectedStateException();
        }
    }

    private static bool CanPrepareIntent(NodeActivationCheckpoint checkpoint, NodeIdentityManifest identity,
        Guid processStartId, Guid requestId, long expectedIncarnation, long recoveryEpoch, ReadOnlySpan<byte> canonicalRequest) =>
        processStartId != Guid.Empty && requestId != Guid.Empty && expectedIncarnation >= 0 && recoveryEpoch > 0 &&
        canonicalRequest.Length is > 0 and <= 64 * 1024 &&
        !checkpoint.ActivationIntents.Any(value => value.ProcessStartId == processStartId || value.ActivationRequestId == requestId) &&
        RequestMatches(canonicalRequest, identity, requestId, expectedIncarnation, recoveryEpoch) &&
        checkpoint.RecoveryView.CurrentIncarnation ==
            (expectedIncarnation == 0 ? null : expectedIncarnation.ToString(CultureInfo.InvariantCulture)) &&
        string.Equals(checkpoint.RecoveryView.RecoveryEpoch, recoveryEpoch.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
        RequestMatchesCheckpointDigest(canonicalRequest, checkpoint.RecoveryCheckpointSha256);

    private static bool IsPersistableReceipt(NodeActivationCheckpoint checkpoint, NodeActivationIntent intent,
        Guid requestId, NodeActivationReceipt receipt, ReadOnlySpan<byte> exactReceipt, out long epoch)
    {
        epoch = 0;
        return intent.State == "ResponseUnknown" && receipt.ActivationRequestId == requestId &&
            receipt.NodeId == checkpoint.NodeId && receipt.InstallationId == checkpoint.InstallationId &&
            receipt.ControlPlaneRealmId == checkpoint.ControlPlaneRealmId && receipt.Status == "Recovering" &&
            receipt.Incarnation > 0 && NodeIdentityManifest.TryPositive(intent.RecoveryEpoch, out epoch) &&
            receipt.RecoveryEpoch == epoch && exactReceipt.Length is > 0 and <= 64 * 1024 &&
            TryNonNegative(intent.ExpectedIncarnation, out var expectedIncarnation) && expectedIncarnation < long.MaxValue &&
            receipt.Incarnation == expectedIncarnation + 1 && receipt.RecoveryCheckpointSha256 == checkpoint.RecoveryCheckpointSha256 &&
            exactReceipt.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(receipt, CanonicalJsonOptions));
    }

    private static bool IsWinningReadbackBound(NodeActivationCurrentReadback readback,
        NodeActivationCheckpoint checkpoint, NodeActivationIntent winner) =>
        readback.NodeId == checkpoint.NodeId && readback.InstallationId == checkpoint.InstallationId &&
        readback.ControlPlaneRealmId == checkpoint.ControlPlaneRealmId &&
        (readback.Status is "Recovering" or "Quiescing" or "Fenced") &&
        readback.RequestSha256 == winner.RequestSha256 &&
        readback.ActivationReceiptJsonBase64 == winner.ReceiptJsonBase64;

    private static bool IsValidWinningIntent(NodeActivationCheckpoint checkpoint,
        NodeActivationCurrentReadback readback, NodeActivationIntent winner, long winningIncarnation, long winningEpoch) =>
        winner.State == "ReceiptPersisted" && IsWinningReadbackBound(readback, checkpoint, winner) &&
        NodeIdentityManifest.TryPositive(winner.RecoveryEpoch, out var winnerEpoch) && winnerEpoch == winningEpoch &&
        TryNonNegative(winner.ExpectedIncarnation, out var expectedIncarnation) && expectedIncarnation < long.MaxValue &&
        winningIncarnation == expectedIncarnation + 1;

    private static bool IsValidGenesisEvidence(NodeIdentityManifest identity, NodeActivationReadbackSnapshot readback,
        NodeExternalAuthorityEvidence authority)
    {
        var current = readback.Readback;
        return IsNoActivationForIdentity(identity, authority, current) && IsValidAuthorityEvidence(identity, authority) &&
            IsExactReadbackEvidence(readback);
    }

    private static bool IsNoActivationForIdentity(NodeIdentityManifest identity, NodeExternalAuthorityEvidence authority,
        NodeActivationCurrentReadback current) =>
        current.Status == "NoActivation" && current.ControlPlaneRealmId == identity.ControlPlaneRealmId &&
        current.NodeId == identity.NodeId && current.InstallationId == identity.InstallationId &&
        current.CredentialRevision == identity.CredentialRevision &&
        current.AuthoritativeRecoveryEpoch == authority.RecoveryEpoch.ToString(CultureInfo.InvariantCulture) &&
        current.RecoveryEpoch is null && current.CurrentIncarnation is null && current.CurrentActivationRequestId is null &&
        current.RequestSha256 is null && current.ActivationReceiptJsonBase64 is null;

    private static bool IsValidAuthorityEvidence(NodeIdentityManifest identity, NodeExternalAuthorityEvidence authority) =>
        authority.ControlPlaneRealmId == identity.ControlPlaneRealmId && authority.RecoveryEpoch > 0 &&
        NodeIdentityManifest.IsDigest(authority.AttestationSha256);

    private static bool IsExactReadbackEvidence(NodeActivationReadbackSnapshot readback) =>
        readback.ExactResponseJson.Length is > 0 and <= 64 * 1024 &&
        string.Equals(Convert.ToHexString(SHA256.HashData(readback.ExactResponseJson)).ToLowerInvariant(),
            readback.ResponseSha256, StringComparison.Ordinal);

    private static NodeIdentityManifest ValidateBundle(NodeVerifiedCredentialBundle bundle)
    {
        if (!HasValidBundleBounds(bundle))
            throw new NodeProtectedStateException();

        var enrollmentReceiptSha256 = Convert.ToHexString(SHA256.HashData(bundle.EnrollmentReceiptJson)).ToLowerInvariant();
        var certificateSha256 = Convert.ToHexString(SHA256.HashData(bundle.CertificateDer)).ToLowerInvariant();
        if (!HasMatchingEnrollmentReceipt(bundle, certificateSha256))
            throw new NodeProtectedStateException();

        ValidateCertificateAndKey(bundle);
        return new NodeIdentityManifest(1, bundle.ControlPlaneRealmId, bundle.NodeId, bundle.InstallationId,
            bundle.CredentialRevision.ToString(CultureInfo.InvariantCulture), certificateSha256, bundle.CredentialSetId,
            enrollmentReceiptSha256, "node-client-key.pem", "node-api-key");
    }

    private static bool HasValidBundleBounds(NodeVerifiedCredentialBundle bundle) =>
        bundle.NodeId != Guid.Empty && bundle.InstallationId != Guid.Empty && bundle.ControlPlaneRealmId != Guid.Empty &&
        bundle.CredentialSetId != Guid.Empty && bundle.CredentialRevision > 0 &&
        bundle.CertificateDer.Length is > 0 and <= 32 * 1024 && bundle.PrivateKeyPem.Length is > 0 and <= 32 * 1024 &&
        bundle.ApiKey.Length is > 0 and <= 256 && bundle.ApiKey.All(value => value is >= '\x21' and <= '\x7E') &&
        bundle.PrivateKeyPem.All(character => !char.IsControl(character) || character is '\r' or '\n');

    private static bool HasMatchingEnrollmentReceipt(NodeVerifiedCredentialBundle bundle, string certificateSha256) =>
        bundle.EnrollmentReceiptJson.Length is > 0 and <= MaximumIdentityBytes &&
        bundle.EnrollmentReceipt.SchemaVersion == 1 && bundle.EnrollmentReceipt.NodeId == bundle.NodeId &&
        bundle.EnrollmentReceipt.InstallationId == bundle.InstallationId &&
        bundle.EnrollmentReceipt.ControlPlaneRealmId == bundle.ControlPlaneRealmId &&
        string.Equals(bundle.EnrollmentReceipt.CertificateSha256, certificateSha256, StringComparison.OrdinalIgnoreCase);

    private static void ValidateCertificateAndKey(NodeVerifiedCredentialBundle bundle)
    {
        using var certificate = X509CertificateLoader.LoadCertificate(bundle.CertificateDer);
        using var publicKey = certificate.GetECDsaPublicKey();
        using var privateKey = ECDsa.Create();
        privateKey.ImportFromPem(bundle.PrivateKeyPem);
        if (publicKey is null || publicKey.KeySize != 256 || privateKey.KeySize != 256 ||
            !CryptographicOperations.FixedTimeEquals(
                publicKey.ExportSubjectPublicKeyInfo(), privateKey.ExportSubjectPublicKeyInfo()))
            throw new NodeProtectedStateException();
    }

    private static bool BytesEqual(byte[]? left, byte[] right) =>
        left is not null && CryptographicOperations.FixedTimeEquals(left, right);

    private static bool RequestMatches(ReadOnlySpan<byte> bytes, NodeIdentityManifest identity, Guid requestId,
        long expectedIncarnation, long recoveryEpoch)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   Guid.TryParseExact(root.GetProperty("activationRequestId").GetString(), "D", out var parsedRequest) && parsedRequest == requestId &&
                   Guid.TryParseExact(root.GetProperty("nodeId").GetString(), "D", out var parsedNode) && parsedNode == identity.NodeId &&
                   Guid.TryParseExact(root.GetProperty("installationId").GetString(), "D", out var parsedInstallation) && parsedInstallation == identity.InstallationId &&
                   Guid.TryParseExact(root.GetProperty("controlPlaneRealmId").GetString(), "D", out var parsedRealm) && parsedRealm == identity.ControlPlaneRealmId &&
                   root.GetProperty("expectedIncarnation").GetString() == expectedIncarnation.ToString(CultureInfo.InvariantCulture) &&
                   root.GetProperty("recoveryEpoch").GetString() == recoveryEpoch.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool RequestMatchesCheckpointDigest(ReadOnlySpan<byte> bytes, string recoveryCheckpointSha256)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            return document.RootElement.GetProperty("recoveryCheckpointSha256").GetString() == recoveryCheckpointSha256;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static List<NodeActivationIntent> CompactSettled(List<NodeActivationIntent> intents, Guid? lastReceiptRequestId) =>
        intents.Where(intent => intent.State is "Prepared" or "ResponseUnknown" ||
            intent.State == "ReceiptPersisted" && intent.ActivationRequestId == lastReceiptRequestId).ToList();

    private static bool TryNonNegative(string value, out long result)
    {
        if (value == "0")
        {
            result = 0;
            return true;
        }
        return NodeIdentityManifest.TryPositive(value, out result);
    }

    private static IReadOnlyList<NodeActivationIntent> ReplaceIntent(IReadOnlyList<NodeActivationIntent> intents,
        NodeActivationIntent updated) => intents.Select(value => value.ActivationRequestId == updated.ActivationRequestId ? updated : value).ToArray();

    internal static JsonSerializerOptions CanonicalJsonOptions => JsonOptions;
}

internal sealed record NodeIdentityManifest(
    int SchemaVersion,
    Guid ControlPlaneRealmId,
    Guid NodeId,
    Guid InstallationId,
    string CredentialRevision,
    string NodeCertificateDerSha256,
    Guid CredentialSetId,
    string EnrollmentReceiptSha256,
    string ClientKeyFile,
    string ApiKeyFile)
{
    public byte[] Serialize()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, NodeLocalStateStore.CanonicalJsonOptions);
        _ = Parse(bytes);
        return bytes;
    }

    public static NodeIdentityManifest Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 16 * 1024)
            throw new NodeProtectedStateException();
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            if (!HasUniqueProperties(document.RootElement,
                    ["schemaVersion", "controlPlaneRealmId", "nodeId", "installationId", "credentialRevision",
                     "nodeCertificateDerSha256", "credentialSetId", "enrollmentReceiptSha256", "clientKeyFile", "apiKeyFile"]))
                throw new NodeProtectedStateException();
            var manifest = JsonSerializer.Deserialize<NodeIdentityManifest>(bytes, NodeLocalStateStore.CanonicalJsonOptions);
            if (manifest is null || manifest.SchemaVersion != 1 || manifest.ControlPlaneRealmId == Guid.Empty ||
                manifest.NodeId == Guid.Empty || manifest.InstallationId == Guid.Empty || manifest.CredentialSetId == Guid.Empty ||
                !TryPositive(manifest.CredentialRevision, out _) || !IsDigest(manifest.NodeCertificateDerSha256) ||
                !IsDigest(manifest.EnrollmentReceiptSha256) || manifest.ClientKeyFile != "node-client-key.pem" ||
                manifest.ApiKeyFile != "node-api-key" ||
                !bytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(manifest, NodeLocalStateStore.CanonicalJsonOptions)))
                throw new NodeProtectedStateException();
            return manifest;
        }
        catch (JsonException)
        {
            throw new NodeProtectedStateException();
        }
    }

    internal static bool HasUniqueProperties(JsonElement root, string[] expected)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return root.EnumerateObject().All(property => names.Add(property.Name)) && names.SetEquals(expected);
    }

    internal static bool TryPositive(string? value, out long result) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result > 0 &&
        result.ToString(CultureInfo.InvariantCulture) == value;

    internal static bool IsDigest(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal sealed record NodeActivationIntent(
    Guid ActivationRequestId,
    Guid ProcessStartId,
    string ExpectedIncarnation,
    string RecoveryEpoch,
    string RequestSha256,
    string RequestJsonBase64,
    string State,
    string? ReceiptJsonBase64,
    string? ReceiptSha256);

internal sealed record NodeActivationCheckpoint(
    int SchemaVersion,
    string CheckpointSequence,
    string CheckpointPayloadSha256,
    Guid ControlPlaneRealmId,
    Guid NodeId,
    Guid InstallationId,
    string LastKnownRecoveryEpoch,
    string? CurrentIncarnation,
    Guid? LastActivationRequestId,
    string? LastActivationReceiptJsonBase64,
    string? LastActivationReceiptSha256,
    string RecoveryViewJsonBase64,
    string RecoveryCheckpointSha256,
    IReadOnlyList<NodeActivationIntent> ActivationIntents,
    Guid? CurrentProcessIntentId)
{
    [JsonIgnore]
    public NodeRecoveryView RecoveryView => NodeRecoveryView.Parse(Convert.FromBase64String(RecoveryViewJsonBase64));

    private static readonly string[] Properties =
    [
        "schemaVersion", "checkpointSequence", "checkpointPayloadSha256", "controlPlaneRealmId", "nodeId",
        "installationId", "lastKnownRecoveryEpoch", "currentIncarnation", "lastActivationRequestId",
        "lastActivationReceiptJsonBase64", "lastActivationReceiptSha256", "recoveryViewJsonBase64",
        "recoveryCheckpointSha256", "activationIntents", "currentProcessIntentId"
    ];

    public byte[] SerializeAndValidate()
    {
        Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, NodeLocalStateStore.CanonicalJsonOptions);
        if (!bytes.AsSpan().SequenceEqual(Parse(bytes).SerializeUnchecked()))
            throw new NodeProtectedStateException();
        return bytes;
    }

    public static NodeActivationCheckpoint Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 16 * 1024 * 1024)
            throw new NodeProtectedStateException();
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            if (!NodeIdentityManifest.HasUniqueProperties(document.RootElement, Properties))
                throw new NodeProtectedStateException();
            var checkpoint = JsonSerializer.Deserialize<NodeActivationCheckpoint>(bytes, NodeLocalStateStore.CanonicalJsonOptions);
            if (checkpoint is null || !bytes.SequenceEqual(checkpoint.SerializeUnchecked()))
                throw new NodeProtectedStateException();
            checkpoint.Validate();
            return checkpoint;
        }
        catch (JsonException)
        {
            throw new NodeProtectedStateException();
        }
    }

    public bool IsBoundTo(NodeIdentityManifest identity) =>
        ControlPlaneRealmId == identity.ControlPlaneRealmId && NodeId == identity.NodeId &&
        InstallationId == identity.InstallationId;

    public static NodeActivationCheckpoint CreateGenesis(NodeIdentityManifest identity, long recoveryEpoch,
        NodeRecoveryView recoveryView)
    {
        if (recoveryEpoch <= 0 || recoveryView.ControlPlaneRealmId != identity.ControlPlaneRealmId ||
            recoveryView.NodeId != identity.NodeId || recoveryView.InstallationId != identity.InstallationId ||
            recoveryView.RecoveryEpoch != recoveryEpoch.ToString(CultureInfo.InvariantCulture) ||
            recoveryView.CurrentIncarnation is not null)
            throw new NodeProtectedStateException();
        var value = new NodeActivationCheckpoint(1, "1", new string('0', 64), identity.ControlPlaneRealmId,
            identity.NodeId, identity.InstallationId, recoveryEpoch.ToString(CultureInfo.InvariantCulture), null,
            null, null, null, Convert.ToBase64String(recoveryView.SerializeAndValidate()), recoveryView.ComputeDigest(),
            [], null);
        value = value with { CheckpointPayloadSha256 = value.ComputePayloadDigest() };
        value.Validate();
        return value;
    }

    public NodeActivationCheckpoint Advance(
        IReadOnlyList<NodeActivationIntent> intents,
        Guid? currentProcessIntentId,
        string lastKnownRecoveryEpoch,
        string? currentIncarnation,
        Guid? lastActivationRequestId,
        string? lastActivationReceiptJsonBase64,
        string? lastActivationReceiptSha256)
    {
        if (!NodeIdentityManifest.TryPositive(CheckpointSequence, out var sequence) || sequence == long.MaxValue)
            throw new NodeProtectedStateException();
        var value = this with
        {
            CheckpointSequence = (sequence + 1).ToString(CultureInfo.InvariantCulture),
            CheckpointPayloadSha256 = new string('0', 64),
            ActivationIntents = intents,
            CurrentProcessIntentId = currentProcessIntentId,
            LastKnownRecoveryEpoch = lastKnownRecoveryEpoch,
            CurrentIncarnation = currentIncarnation,
            LastActivationRequestId = lastActivationRequestId,
            LastActivationReceiptJsonBase64 = lastActivationReceiptJsonBase64,
            LastActivationReceiptSha256 = lastActivationReceiptSha256
        };
        value = value with { CheckpointPayloadSha256 = value.ComputePayloadDigest() };
        value.Validate();
        return value;
    }

    private byte[] SerializeUnchecked() => JsonSerializer.SerializeToUtf8Bytes(this, NodeLocalStateStore.CanonicalJsonOptions);

    private void Validate()
    {
        ValidateEnvelope();
        ValidateIntentLedger();
        ValidateCurrentIntentPointer();
        ValidateLastReceipt();
        if (!string.Equals(ComputePayloadDigest(), CheckpointPayloadSha256, StringComparison.Ordinal))
            throw new NodeProtectedStateException();
    }

    private void ValidateEnvelope()
    {
        if (!HasValidCheckpointHeader() || !HasValidCheckpointState())
            throw new NodeProtectedStateException();
        var recoveryView = RecoveryView;
        if (recoveryView.ControlPlaneRealmId != ControlPlaneRealmId || recoveryView.NodeId != NodeId ||
            recoveryView.InstallationId != InstallationId || recoveryView.ComputeDigest() != RecoveryCheckpointSha256)
            throw new NodeProtectedStateException();
    }

    private bool HasValidCheckpointHeader() =>
        SchemaVersion == 1 && ControlPlaneRealmId != Guid.Empty && NodeId != Guid.Empty && InstallationId != Guid.Empty &&
        NodeIdentityManifest.TryPositive(CheckpointSequence, out _) && NodeIdentityManifest.IsDigest(CheckpointPayloadSha256);

    private bool HasValidCheckpointState() => NodeIdentityManifest.TryPositive(LastKnownRecoveryEpoch, out _) &&
        HasValidIncarnation() && HasValidRecoveryView() && ActivationIntents is not null && ActivationIntents.Count <= 256;

    private bool HasValidIncarnation() =>
        CurrentIncarnation is null || NodeIdentityManifest.TryPositive(CurrentIncarnation, out _);

    private bool HasValidRecoveryView() =>
        NodeIdentityManifest.IsDigest(RecoveryCheckpointSha256) &&
        TryDecodeCanonicalBase64(RecoveryViewJsonBase64, out var bytes) && bytes.Length <= 16 * 1024;

    private void ValidateIntentLedger()
    {
        if (ActivationIntents.Select(intent => intent.ActivationRequestId).Distinct().Count() != ActivationIntents.Count ||
            ActivationIntents.Select(intent => intent.ProcessStartId).Distinct().Count() != ActivationIntents.Count ||
            !ActivationIntents.All(IsValidIntent))
            throw new NodeProtectedStateException();
    }

    private static bool IsValidIntent(NodeActivationIntent intent)
    {
        if (intent.ActivationRequestId == Guid.Empty || intent.ProcessStartId == Guid.Empty ||
            !(intent.ExpectedIncarnation == "0" || NodeIdentityManifest.TryPositive(intent.ExpectedIncarnation, out _)) ||
            !NodeIdentityManifest.TryPositive(intent.RecoveryEpoch, out _) || !NodeIdentityManifest.IsDigest(intent.RequestSha256) ||
            intent.State is not ("Prepared" or "ResponseUnknown" or "ReceiptPersisted" or "FencedOut" or "AbandonedUnsent") ||
            !TryDecodeCanonicalBase64(intent.RequestJsonBase64, out var requestBytes) || requestBytes.Length > 64 * 1024 ||
            !DigestMatches(requestBytes, intent.RequestSha256))
            return false;
        return HasValidIntentReceipt(intent);
    }

    private static bool HasValidIntentReceipt(NodeActivationIntent intent)
    {
        if (intent.State != "ReceiptPersisted")
            return intent.ReceiptJsonBase64 is null && intent.ReceiptSha256 is null;
        return TryDecodeCanonicalBase64(intent.ReceiptJsonBase64, out var receiptBytes) &&
            NodeIdentityManifest.IsDigest(intent.ReceiptSha256) && DigestMatches(receiptBytes, intent.ReceiptSha256!);
    }

    private void ValidateCurrentIntentPointer()
    {
        if (CurrentProcessIntentId is Guid current && !ActivationIntents.Any(intent => intent.ActivationRequestId == current))
            throw new NodeProtectedStateException();
    }

    private void ValidateLastReceipt()
    {
        if ((LastActivationRequestId is null) != (LastActivationReceiptSha256 is null) ||
            (LastActivationReceiptJsonBase64 is null) != (LastActivationReceiptSha256 is null))
            throw new NodeProtectedStateException();
        if (LastActivationReceiptJsonBase64 is not null &&
            (!TryDecodeCanonicalBase64(LastActivationReceiptJsonBase64, out var receipt) ||
             !NodeIdentityManifest.IsDigest(LastActivationReceiptSha256) ||
             !DigestMatches(receipt, LastActivationReceiptSha256!)))
            throw new NodeProtectedStateException();
    }

    private string ComputePayloadDigest()
    {
        var payload = new NodeActivationCheckpointPayload(SchemaVersion, CheckpointSequence, ControlPlaneRealmId,
            NodeId, InstallationId, LastKnownRecoveryEpoch, CurrentIncarnation, LastActivationRequestId,
            LastActivationReceiptJsonBase64, LastActivationReceiptSha256, RecoveryViewJsonBase64,
            RecoveryCheckpointSha256, ActivationIntents, CurrentProcessIntentId);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload,
            NodeLocalStateStore.CanonicalJsonOptions))).ToLowerInvariant();
    }

    private static bool TryDecodeCanonicalBase64(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length > 96 * 1024)
            return false;
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

    private static bool DigestMatches(byte[] bytes, string digest) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(digest));
}

internal sealed record NodeActivationCheckpointPayload(
    int SchemaVersion,
    string CheckpointSequence,
    Guid ControlPlaneRealmId,
    Guid NodeId,
    Guid InstallationId,
    string LastKnownRecoveryEpoch,
    string? CurrentIncarnation,
    Guid? LastActivationRequestId,
    string? LastActivationReceiptJsonBase64,
    string? LastActivationReceiptSha256,
    string RecoveryViewJsonBase64,
    string RecoveryCheckpointSha256,
    IReadOnlyList<NodeActivationIntent> ActivationIntents,
    Guid? CurrentProcessIntentId);

internal sealed record NodeRecoveryView(
    int SchemaVersion,
    Guid ControlPlaneRealmId,
    Guid NodeId,
    Guid InstallationId,
    string RecoveryEpoch,
    string? CurrentIncarnation,
    string JournalInventorySha256,
    string UnresolvedOperationSetSha256,
    string RecoveryEvidenceSha256)
{
    private static readonly string[] Properties =
    ["schemaVersion", "controlPlaneRealmId", "nodeId", "installationId", "recoveryEpoch", "currentIncarnation",
        "journalInventorySha256", "unresolvedOperationSetSha256", "recoveryEvidenceSha256"];

    public static NodeRecoveryView Create(NodeIdentityManifest identity, long epoch, string? incarnation,
        string journalDigest, string unresolvedDigest, string evidenceDigest)
    {
        var view = new NodeRecoveryView(1, identity.ControlPlaneRealmId, identity.NodeId, identity.InstallationId,
            epoch.ToString(CultureInfo.InvariantCulture), incarnation, journalDigest, unresolvedDigest, evidenceDigest);
        _ = view.SerializeAndValidate();
        return view;
    }

    public static NodeRecoveryView Parse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            if (!NodeIdentityManifest.HasUniqueProperties(document.RootElement, Properties))
                throw new NodeProtectedStateException();
            var view = JsonSerializer.Deserialize<NodeRecoveryView>(bytes, NodeLocalStateStore.CanonicalJsonOptions);
            if (view is null || !bytes.SequenceEqual(view.SerializeAndValidate()))
                throw new NodeProtectedStateException();
            return view;
        }
        catch (JsonException)
        {
            throw new NodeProtectedStateException();
        }
    }

    public byte[] SerializeAndValidate()
    {
        if (SchemaVersion != 1 || ControlPlaneRealmId == Guid.Empty || NodeId == Guid.Empty || InstallationId == Guid.Empty ||
            !NodeIdentityManifest.TryPositive(RecoveryEpoch, out _) ||
            (CurrentIncarnation is not null && !NodeIdentityManifest.TryPositive(CurrentIncarnation, out _) ) ||
            !NodeIdentityManifest.IsDigest(JournalInventorySha256) || !NodeIdentityManifest.IsDigest(UnresolvedOperationSetSha256) ||
            !NodeIdentityManifest.IsDigest(RecoveryEvidenceSha256))
            throw new NodeProtectedStateException();
        return JsonSerializer.SerializeToUtf8Bytes(this, NodeLocalStateStore.CanonicalJsonOptions);
    }

    public string ComputeDigest() => Convert.ToHexString(SHA256.HashData(SerializeAndValidate())).ToLowerInvariant();
}

internal sealed record NodeFreshEnrollmentGenesisEvidence(
    int SchemaVersion,
    string EnrollmentReceiptSha256,
    string ExternalRegisterAttestationSha256,
    string NoActivationReadbackSha256);

internal sealed record NodeLocalStateSnapshot(NodeIdentityManifest? Identity, NodeActivationCheckpoint? Checkpoint);

internal sealed class NodeLocalClientCredentials(NodeIdentityManifest identity, X509Certificate2 certificate,
    string apiKey) : IDisposable
{
    public NodeIdentityManifest Identity { get; } = identity;
    public X509Certificate2 Certificate { get; } = certificate;
    public string ApiKey { get; } = apiKey;

    public void Dispose() => Certificate.Dispose();
}

internal sealed record NodeEnrollmentReceiptEvidence(
    int SchemaVersion,
    Guid NodeId,
    Guid InstallationId,
    Guid ControlPlaneRealmId,
    string CertificateSha256);

internal sealed record NodeVerifiedCredentialBundle(
    Guid NodeId,
    Guid InstallationId,
    Guid ControlPlaneRealmId,
    long CredentialRevision,
    Guid CredentialSetId,
    byte[] CertificateDer,
    string PrivateKeyPem,
    string ApiKey,
    NodeEnrollmentReceiptEvidence EnrollmentReceipt,
    byte[] EnrollmentReceiptJson);
