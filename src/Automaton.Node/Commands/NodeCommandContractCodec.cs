using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Node.Commands;

public static class NodeCommandContractCodec
{
    public const int MaximumDocumentBytes = 1024 * 1024;
    public const string StatusCommandKind = "inspect-node-status-v1";
    private const string NodeManagementScope = "node-management";
    private const string RecoveryRequiredPhase = "RecoveryRequired";
    private const string ResultStoredStatus = "ResultStored";
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static byte[] SerializeIntent(NodeCommandIntentV1 intent)
    {
        ValidateIntent(intent);
        return JsonSerializer.SerializeToUtf8Bytes(intent, JsonOptions);
    }

    public static string ComputeIntentSha256(NodeCommandIntentV1 intent) => Hash(SerializeIntent(intent));

    public static NodeStatusCommandV1 SealCommand(NodeStatusCommandV1 command)
    {
        ValidateCommandShape(command);
        return command with { RequestSha256 = Hash(SerializeCommandHashInput(command)) };
    }

    public static byte[] SerializeCommand(NodeStatusCommandV1 command)
    {
        ValidateCommandShape(command);
        RequireHash(command.RequestSha256, Hash(SerializeCommandHashInput(command)));
        return SerializeBounded(command);
    }

    public static NodeStatusCommandV1 ParseCommand(ReadOnlySpan<byte> bytes)
    {
        var command = ParseCanonical<NodeStatusCommandV1>(bytes,
            ["schemaVersion", "commandId", "clientRequestId", "kind", "target", "issuedAtUtc", "deadlineUtc", "requestSha256"]);
        ValidateCommandShape(command);
        RequireHash(command.RequestSha256, Hash(SerializeCommandHashInput(command)));
        return command;
    }

    public static void ValidateCommandTime(NodeStatusCommandV1 command, DateTimeOffset receiverUtcNow)
    {
        _ = SerializeCommand(command);
        var issuedAt = ParseUtc(command.IssuedAtUtc);
        var deadline = ParseUtc(command.DeadlineUtc);
        if (issuedAt > receiverUtcNow.AddSeconds(30) || deadline <= receiverUtcNow || deadline <= issuedAt ||
            deadline > issuedAt.AddMinutes(5))
            throw InvalidContract();
    }

    public static NodeStatusResultV1 SealResult(NodeStatusResultV1 result)
    {
        ValidateResultShape(result);
        return result with { ResultSha256 = Hash(SerializeResultHashInput(result)) };
    }

    public static byte[] SerializeResult(NodeStatusResultV1 result)
    {
        ValidateResultShape(result);
        RequireHash(result.ResultSha256, Hash(SerializeResultHashInput(result)));
        return SerializeBounded(result);
    }

    public static NodeStatusResultV1 ParseResult(ReadOnlySpan<byte> bytes)
    {
        var result = ParseCanonical<NodeStatusResultV1>(bytes,
            ["schemaVersion", "commandId", "requestSha256", "nodeId", "installationId", "controlPlaneRealmId",
             "recoveryEpoch", "originalIncarnation", "credentialRevision", "reportingIncarnation", "observedAtUtc",
             "startupPhase", "activationReceiptSha256", "checkpointSha256", "resultSha256"]);
        ValidateResultShape(result);
        RequireHash(result.ResultSha256, Hash(SerializeResultHashInput(result)));
        return result;
    }

    public static void ValidateResultBinding(NodeStatusCommandV1 command, NodeStatusResultV1 result,
        NodeCommandBinding binding, DateTimeOffset serverReceivedAtUtc)
    {
        _ = SerializeCommand(command);
        _ = SerializeResult(result);
        ValidateBinding(command.Target, binding);
        if (result.CommandId != command.CommandId || result.RequestSha256 != command.RequestSha256 ||
            result.NodeId != binding.NodeId || result.InstallationId != binding.InstallationId ||
            result.ControlPlaneRealmId != binding.ControlPlaneRealmId || result.RecoveryEpoch != Canonical(binding.RecoveryEpoch) ||
            result.OriginalIncarnation != command.Target.Incarnation ||
            result.OriginalIncarnation != Canonical(binding.Incarnation) ||
            result.ReportingIncarnation != result.OriginalIncarnation ||
            result.CredentialRevision != Canonical(binding.CredentialRevision) ||
            result.StartupPhase != RecoveryRequiredPhase)
            throw InvalidContract();

        var observedAt = ParseUtc(result.ObservedAtUtc);
        var issuedAt = ParseUtc(command.IssuedAtUtc);
        var deadline = ParseUtc(command.DeadlineUtc);
        if (observedAt < issuedAt.AddSeconds(-30) || observedAt > deadline || observedAt > serverReceivedAtUtc.AddSeconds(30))
            throw InvalidContract();
    }

    public static NodeCommandResultReceiptV1 SealReceipt(NodeCommandResultReceiptV1 receipt)
    {
        ValidateReceiptShape(receipt);
        return receipt with { ReceiptSha256 = Hash(SerializeReceiptHashInput(receipt)) };
    }

    public static byte[] SerializeReceipt(NodeCommandResultReceiptV1 receipt)
    {
        ValidateReceiptShape(receipt);
        RequireHash(receipt.ReceiptSha256, Hash(SerializeReceiptHashInput(receipt)));
        return SerializeBounded(receipt);
    }

    public static NodeCommandResultReceiptV1 ParseReceipt(ReadOnlySpan<byte> bytes)
    {
        var receipt = ParseCanonical<NodeCommandResultReceiptV1>(bytes,
            ["schemaVersion", "receiptId", "commandId", "nodeId", "installationId", "controlPlaneRealmId",
             "recoveryEpoch", "credentialRevision", "originalIncarnation", "reportingIncarnation", "resultSha256",
             "status", "receivedAtUtc", "storedAtUtc", "receiptSha256"]);
        ValidateReceiptShape(receipt);
        RequireHash(receipt.ReceiptSha256, Hash(SerializeReceiptHashInput(receipt)));
        return receipt;
    }

    public static void ValidateReceiptBinding(NodeStatusCommandV1 command, NodeStatusResultV1 result,
        NodeCommandResultReceiptV1 receipt, NodeCommandBinding binding)
    {
        _ = SerializeCommand(command);
        _ = SerializeResult(result);
        _ = SerializeReceipt(receipt);
        ValidateResultBinding(command, result, binding, ParseUtc(receipt.ReceivedAtUtc));
        ValidateBinding(command.Target, binding);
        if (receipt.CommandId != command.CommandId || receipt.NodeId != binding.NodeId ||
            receipt.InstallationId != binding.InstallationId || receipt.ControlPlaneRealmId != binding.ControlPlaneRealmId ||
            receipt.RecoveryEpoch != Canonical(binding.RecoveryEpoch) ||
            receipt.CredentialRevision != Canonical(binding.CredentialRevision) ||
            receipt.OriginalIncarnation != command.Target.Incarnation ||
            receipt.OriginalIncarnation != Canonical(binding.Incarnation) ||
            receipt.ReportingIncarnation != receipt.OriginalIncarnation || receipt.ResultSha256 != result.ResultSha256 ||
            receipt.Status != ResultStoredStatus || ParseUtc(receipt.StoredAtUtc) < ParseUtc(receipt.ReceivedAtUtc))
            throw InvalidContract();
    }

    private static byte[] SerializeCommandHashInput(NodeStatusCommandV1 value) => JsonSerializer.SerializeToUtf8Bytes(
        new CommandHashInputV1(value.SchemaVersion, value.CommandId, value.ClientRequestId, value.Kind, value.Target,
            value.IssuedAtUtc, value.DeadlineUtc), JsonOptions);

    private static byte[] SerializeResultHashInput(NodeStatusResultV1 value) => JsonSerializer.SerializeToUtf8Bytes(
        new ResultHashInputV1(value.SchemaVersion, value.CommandId, value.RequestSha256, value.NodeId,
            value.InstallationId, value.ControlPlaneRealmId, value.RecoveryEpoch, value.OriginalIncarnation,
            value.CredentialRevision, value.ReportingIncarnation, value.ObservedAtUtc, value.StartupPhase,
            value.ActivationReceiptSha256, value.CheckpointSha256), JsonOptions);

    private static byte[] SerializeReceiptHashInput(NodeCommandResultReceiptV1 value) => JsonSerializer.SerializeToUtf8Bytes(
        new ReceiptHashInputV1(value.SchemaVersion, value.ReceiptId, value.CommandId, value.NodeId,
            value.InstallationId, value.ControlPlaneRealmId, value.RecoveryEpoch, value.CredentialRevision,
            value.OriginalIncarnation, value.ReportingIncarnation, value.ResultSha256, value.Status,
            value.ReceivedAtUtc, value.StoredAtUtc), JsonOptions);

    private static T ParseCanonical<T>(ReadOnlySpan<byte> bytes, string[] requiredProperties)
    {
        if (bytes.Length is 0 or > MaximumDocumentBytes)
            throw InvalidContract();
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object || !HasUniqueProperties(document.RootElement))
                throw InvalidContract();
            var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            if (!names.SequenceEqual(requiredProperties, StringComparer.Ordinal))
                throw InvalidContract();
            var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
            if (value is null || !bytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)))
                throw InvalidContract();
            return value;
        }
        catch (JsonException)
        {
            throw InvalidContract();
        }
    }

    private static bool HasUniqueProperties(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => HasUniqueObjectProperties(element),
            JsonValueKind.Array => element.EnumerateArray().All(HasUniqueProperties),
            _ => true
        };
    }

    private static bool HasUniqueObjectProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().All(property => names.Add(property.Name) && HasUniqueProperties(property.Value));
    }

    private static void ValidateIntent(NodeCommandIntentV1 intent)
    {
        if (intent.SchemaVersion != 1 || intent.Kind != StatusCommandKind || intent.NodeId == Guid.Empty ||
            intent.InstallationId == Guid.Empty || intent.View != "status")
            throw InvalidContract();
    }

    private static void ValidateCommandShape(NodeStatusCommandV1 command)
    {
        if (command.SchemaVersion != 1 || command.CommandId == Guid.Empty || command.ClientRequestId == Guid.Empty ||
            command.Kind != StatusCommandKind)
            throw InvalidContract();
        ValidateCommandTarget(command.Target);
        ValidateCommandTimeFields(command);
    }

    private static void ValidateCommandTarget(NodeCommandTargetV1? target)
    {
        if (target is null || target.Scope != NodeManagementScope || target.NodeId == Guid.Empty ||
            target.InstallationId == Guid.Empty || target.ControlPlaneRealmId == Guid.Empty ||
            !IsPositive(target.RecoveryEpoch) || !IsPositive(target.Incarnation) || !IsPositive(target.CredentialRevision))
            throw InvalidContract();
    }

    private static void ValidateCommandTimeFields(NodeStatusCommandV1 command)
    {
        if (!IsSha256(command.RequestSha256) || !IsUtc(command.IssuedAtUtc) || !IsUtc(command.DeadlineUtc))
            throw InvalidContract();
        var issuedAt = ParseUtc(command.IssuedAtUtc);
        var deadline = ParseUtc(command.DeadlineUtc);
        if (deadline <= issuedAt || deadline > issuedAt.AddMinutes(5))
            throw InvalidContract();
    }

    private static void ValidateResultShape(NodeStatusResultV1 result)
    {
        if (result.SchemaVersion != 1 || result.CommandId == Guid.Empty || result.NodeId == Guid.Empty ||
            result.InstallationId == Guid.Empty || result.ControlPlaneRealmId == Guid.Empty)
            throw InvalidContract();
        ValidateResultIdentityFields(result);
        ValidateResultEvidenceFields(result);
    }

    private static void ValidateResultIdentityFields(NodeStatusResultV1 result)
    {
        if (!IsSha256(result.RequestSha256) || !IsPositive(result.RecoveryEpoch) ||
            !IsPositive(result.OriginalIncarnation) || !IsPositive(result.CredentialRevision) ||
            !IsPositive(result.ReportingIncarnation))
            throw InvalidContract();
    }

    private static void ValidateResultEvidenceFields(NodeStatusResultV1 result)
    {
        if (!IsUtc(result.ObservedAtUtc) || result.StartupPhase != RecoveryRequiredPhase ||
            !IsSha256(result.ActivationReceiptSha256) || !IsSha256(result.CheckpointSha256) || !IsSha256(result.ResultSha256))
            throw InvalidContract();
        _ = ParseUtc(result.ObservedAtUtc);
    }

    private static void ValidateReceiptShape(NodeCommandResultReceiptV1 receipt)
    {
        if (receipt.SchemaVersion != 1 || receipt.ReceiptId == Guid.Empty || receipt.CommandId == Guid.Empty ||
            receipt.NodeId == Guid.Empty || receipt.InstallationId == Guid.Empty || receipt.ControlPlaneRealmId == Guid.Empty)
            throw InvalidContract();
        ValidateReceiptIdentityFields(receipt);
        ValidateReceiptResultFields(receipt);
    }

    private static void ValidateReceiptIdentityFields(NodeCommandResultReceiptV1 receipt)
    {
        if (!IsPositive(receipt.RecoveryEpoch) || !IsPositive(receipt.CredentialRevision) ||
            !IsPositive(receipt.OriginalIncarnation) || !IsPositive(receipt.ReportingIncarnation))
            throw InvalidContract();
    }

    private static void ValidateReceiptResultFields(NodeCommandResultReceiptV1 receipt)
    {
        if (!IsSha256(receipt.ResultSha256) || receipt.Status != ResultStoredStatus ||
            !IsUtc(receipt.ReceivedAtUtc) || !IsUtc(receipt.StoredAtUtc) || !IsSha256(receipt.ReceiptSha256))
            throw InvalidContract();
        var receivedAt = ParseUtc(receipt.ReceivedAtUtc);
        var storedAt = ParseUtc(receipt.StoredAtUtc);
        if (storedAt < receivedAt)
            throw InvalidContract();
    }

    private static void ValidateBinding(NodeCommandTargetV1 target, NodeCommandBinding binding)
    {
        if (binding.NodeId == Guid.Empty || binding.InstallationId == Guid.Empty || binding.ControlPlaneRealmId == Guid.Empty ||
            binding.RecoveryEpoch <= 0 || binding.Incarnation <= 0 || binding.CredentialRevision <= 0 ||
            target.NodeId != binding.NodeId || target.InstallationId != binding.InstallationId ||
            target.ControlPlaneRealmId != binding.ControlPlaneRealmId || target.RecoveryEpoch != Canonical(binding.RecoveryEpoch) ||
            target.Incarnation != Canonical(binding.Incarnation) || target.CredentialRevision != Canonical(binding.CredentialRevision))
            throw InvalidContract();
    }

    private static bool IsPositive(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
        parsed > 0 && Canonical(parsed) == value;

    private static string Canonical(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool IsUtc(string? value) => value is { Length: 28 } && value.EndsWith('Z') &&
        DateTimeOffset.TryParseExact(value, UtcFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _);

    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.TryParseExact(value, UtcFormat,
        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
        ? parsed : throw InvalidContract();

    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RequireHash(string actual, string expected)
    {
        if (!IsSha256(actual) || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)))
            throw InvalidContract();
    }

    private static byte[] SerializeBounded<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return bytes.Length is > 0 and <= MaximumDocumentBytes ? bytes : throw InvalidContract();
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static InvalidDataException InvalidContract() => new("Node command contract is invalid.");

    private sealed record CommandHashInputV1(
        [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] int SchemaVersion,
        [property: JsonPropertyName("commandId"), JsonPropertyOrder(1)] Guid CommandId,
        [property: JsonPropertyName("clientRequestId"), JsonPropertyOrder(2)] Guid ClientRequestId,
        [property: JsonPropertyName("kind"), JsonPropertyOrder(3)] string Kind,
        [property: JsonPropertyName("target"), JsonPropertyOrder(4)] NodeCommandTargetV1 Target,
        [property: JsonPropertyName("issuedAtUtc"), JsonPropertyOrder(5)] string IssuedAtUtc,
        [property: JsonPropertyName("deadlineUtc"), JsonPropertyOrder(6)] string DeadlineUtc);

    private sealed record ResultHashInputV1(
        [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] int SchemaVersion,
        [property: JsonPropertyName("commandId"), JsonPropertyOrder(1)] Guid CommandId,
        [property: JsonPropertyName("requestSha256"), JsonPropertyOrder(2)] string RequestSha256,
        [property: JsonPropertyName("nodeId"), JsonPropertyOrder(3)] Guid NodeId,
        [property: JsonPropertyName("installationId"), JsonPropertyOrder(4)] Guid InstallationId,
        [property: JsonPropertyName("controlPlaneRealmId"), JsonPropertyOrder(5)] Guid ControlPlaneRealmId,
        [property: JsonPropertyName("recoveryEpoch"), JsonPropertyOrder(6)] string RecoveryEpoch,
        [property: JsonPropertyName("originalIncarnation"), JsonPropertyOrder(7)] string OriginalIncarnation,
        [property: JsonPropertyName("credentialRevision"), JsonPropertyOrder(8)] string CredentialRevision,
        [property: JsonPropertyName("reportingIncarnation"), JsonPropertyOrder(9)] string ReportingIncarnation,
        [property: JsonPropertyName("observedAtUtc"), JsonPropertyOrder(10)] string ObservedAtUtc,
        [property: JsonPropertyName("startupPhase"), JsonPropertyOrder(11)] string StartupPhase,
        [property: JsonPropertyName("activationReceiptSha256"), JsonPropertyOrder(12)] string ActivationReceiptSha256,
        [property: JsonPropertyName("checkpointSha256"), JsonPropertyOrder(13)] string CheckpointSha256);

    private sealed record ReceiptHashInputV1(
        [property: JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] int SchemaVersion,
        [property: JsonPropertyName("receiptId"), JsonPropertyOrder(1)] Guid ReceiptId,
        [property: JsonPropertyName("commandId"), JsonPropertyOrder(2)] Guid CommandId,
        [property: JsonPropertyName("nodeId"), JsonPropertyOrder(3)] Guid NodeId,
        [property: JsonPropertyName("installationId"), JsonPropertyOrder(4)] Guid InstallationId,
        [property: JsonPropertyName("controlPlaneRealmId"), JsonPropertyOrder(5)] Guid ControlPlaneRealmId,
        [property: JsonPropertyName("recoveryEpoch"), JsonPropertyOrder(6)] string RecoveryEpoch,
        [property: JsonPropertyName("credentialRevision"), JsonPropertyOrder(7)] string CredentialRevision,
        [property: JsonPropertyName("originalIncarnation"), JsonPropertyOrder(8)] string OriginalIncarnation,
        [property: JsonPropertyName("reportingIncarnation"), JsonPropertyOrder(9)] string ReportingIncarnation,
        [property: JsonPropertyName("resultSha256"), JsonPropertyOrder(10)] string ResultSha256,
        [property: JsonPropertyName("status"), JsonPropertyOrder(11)] string Status,
        [property: JsonPropertyName("receivedAtUtc"), JsonPropertyOrder(12)] string ReceivedAtUtc,
        [property: JsonPropertyName("storedAtUtc"), JsonPropertyOrder(13)] string StoredAtUtc);
}
