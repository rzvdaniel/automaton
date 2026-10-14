using System.Text.Json.Serialization;

namespace Automaton.Node.Commands;

public sealed record NodeCommandBinding(Guid NodeId, Guid InstallationId, Guid ControlPlaneRealmId,
    long RecoveryEpoch, long Incarnation, long CredentialRevision);

public sealed record NodeCommandIntentV1
{
    [JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] public required int SchemaVersion { get; init; }
    [JsonPropertyName("kind"), JsonPropertyOrder(1)] public required string Kind { get; init; }
    [JsonPropertyName("nodeId"), JsonPropertyOrder(2)] public required Guid NodeId { get; init; }
    [JsonPropertyName("installationId"), JsonPropertyOrder(3)] public required Guid InstallationId { get; init; }
    [JsonPropertyName("view"), JsonPropertyOrder(4)] public required string View { get; init; }
}

public sealed record NodeCommandTargetV1
{
    [JsonPropertyName("scope"), JsonPropertyOrder(0)] public required string Scope { get; init; }
    [JsonPropertyName("nodeId"), JsonPropertyOrder(1)] public required Guid NodeId { get; init; }
    [JsonPropertyName("installationId"), JsonPropertyOrder(2)] public required Guid InstallationId { get; init; }
    [JsonPropertyName("controlPlaneRealmId"), JsonPropertyOrder(3)] public required Guid ControlPlaneRealmId { get; init; }
    [JsonPropertyName("recoveryEpoch"), JsonPropertyOrder(4)] public required string RecoveryEpoch { get; init; }
    [JsonPropertyName("incarnation"), JsonPropertyOrder(5)] public required string Incarnation { get; init; }
    [JsonPropertyName("credentialRevision"), JsonPropertyOrder(6)] public required string CredentialRevision { get; init; }
}

public sealed record NodeStatusCommandV1
{
    [JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] public required int SchemaVersion { get; init; }
    [JsonPropertyName("commandId"), JsonPropertyOrder(1)] public required Guid CommandId { get; init; }
    [JsonPropertyName("clientRequestId"), JsonPropertyOrder(2)] public required Guid ClientRequestId { get; init; }
    [JsonPropertyName("kind"), JsonPropertyOrder(3)] public required string Kind { get; init; }
    [JsonPropertyName("target"), JsonPropertyOrder(4)] public required NodeCommandTargetV1 Target { get; init; }
    [JsonPropertyName("issuedAtUtc"), JsonPropertyOrder(5)] public required string IssuedAtUtc { get; init; }
    [JsonPropertyName("deadlineUtc"), JsonPropertyOrder(6)] public required string DeadlineUtc { get; init; }
    [JsonPropertyName("requestSha256"), JsonPropertyOrder(7)] public required string RequestSha256 { get; init; }
}

public sealed record NodeStatusResultV1
{
    [JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] public required int SchemaVersion { get; init; }
    [JsonPropertyName("commandId"), JsonPropertyOrder(1)] public required Guid CommandId { get; init; }
    [JsonPropertyName("requestSha256"), JsonPropertyOrder(2)] public required string RequestSha256 { get; init; }
    [JsonPropertyName("nodeId"), JsonPropertyOrder(3)] public required Guid NodeId { get; init; }
    [JsonPropertyName("installationId"), JsonPropertyOrder(4)] public required Guid InstallationId { get; init; }
    [JsonPropertyName("controlPlaneRealmId"), JsonPropertyOrder(5)] public required Guid ControlPlaneRealmId { get; init; }
    [JsonPropertyName("recoveryEpoch"), JsonPropertyOrder(6)] public required string RecoveryEpoch { get; init; }
    [JsonPropertyName("originalIncarnation"), JsonPropertyOrder(7)] public required string OriginalIncarnation { get; init; }
    [JsonPropertyName("credentialRevision"), JsonPropertyOrder(8)] public required string CredentialRevision { get; init; }
    [JsonPropertyName("reportingIncarnation"), JsonPropertyOrder(9)] public required string ReportingIncarnation { get; init; }
    [JsonPropertyName("observedAtUtc"), JsonPropertyOrder(10)] public required string ObservedAtUtc { get; init; }
    [JsonPropertyName("startupPhase"), JsonPropertyOrder(11)] public required string StartupPhase { get; init; }
    [JsonPropertyName("activationReceiptSha256"), JsonPropertyOrder(12)] public required string ActivationReceiptSha256 { get; init; }
    [JsonPropertyName("checkpointSha256"), JsonPropertyOrder(13)] public required string CheckpointSha256 { get; init; }
    [JsonPropertyName("resultSha256"), JsonPropertyOrder(14)] public required string ResultSha256 { get; init; }
}

public sealed record NodeCommandResultReceiptV1
{
    [JsonPropertyName("schemaVersion"), JsonPropertyOrder(0)] public required int SchemaVersion { get; init; }
    [JsonPropertyName("receiptId"), JsonPropertyOrder(1)] public required Guid ReceiptId { get; init; }
    [JsonPropertyName("commandId"), JsonPropertyOrder(2)] public required Guid CommandId { get; init; }
    [JsonPropertyName("nodeId"), JsonPropertyOrder(3)] public required Guid NodeId { get; init; }
    [JsonPropertyName("installationId"), JsonPropertyOrder(4)] public required Guid InstallationId { get; init; }
    [JsonPropertyName("controlPlaneRealmId"), JsonPropertyOrder(5)] public required Guid ControlPlaneRealmId { get; init; }
    [JsonPropertyName("recoveryEpoch"), JsonPropertyOrder(6)] public required string RecoveryEpoch { get; init; }
    [JsonPropertyName("credentialRevision"), JsonPropertyOrder(7)] public required string CredentialRevision { get; init; }
    [JsonPropertyName("originalIncarnation"), JsonPropertyOrder(8)] public required string OriginalIncarnation { get; init; }
    [JsonPropertyName("reportingIncarnation"), JsonPropertyOrder(9)] public required string ReportingIncarnation { get; init; }
    [JsonPropertyName("resultSha256"), JsonPropertyOrder(10)] public required string ResultSha256 { get; init; }
    [JsonPropertyName("status"), JsonPropertyOrder(11)] public required string Status { get; init; }
    [JsonPropertyName("receivedAtUtc"), JsonPropertyOrder(12)] public required string ReceivedAtUtc { get; init; }
    [JsonPropertyName("storedAtUtc"), JsonPropertyOrder(13)] public required string StoredAtUtc { get; init; }
    [JsonPropertyName("receiptSha256"), JsonPropertyOrder(14)] public required string ReceiptSha256 { get; init; }
}
