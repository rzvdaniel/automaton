using System.Security.Cryptography;
using System.Text;
using Automaton.Node.Commands;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeCommandContractCodecTests
{
    private static readonly Guid CommandId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientRequestId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid NodeId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid RealmId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid InstallationId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid ReceiptId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset IssuedAt = Utc("2026-10-14T12:00:00.0000000Z");
    private static readonly DateTimeOffset Deadline = Utc("2026-10-14T12:05:00.0000000Z");
    private static readonly DateTimeOffset ObservedAt = Utc("2026-10-14T12:00:01.0000000Z");
    private const string CommandHash = "963dc18e67b9f72ad794c5687994961c3a669719aff09ef2158be3ac8594d181";
    private const string ResultHash = "e0cb44b49ee3f83e29475d1ed3345a9da7cb266b9a568d3856d3ca53999400ee";
    private const string ReceiptHash = "dee912a14ce5f28d7f0c967980a741614082d05966f51279f46329b732b9520e";

    [Test]
    public void Intent_command_result_and_receipt_match_fixed_utf8_hash_vectors()
    {
        var intent = new NodeCommandIntentV1
        {
            SchemaVersion = 1,
            Kind = NodeCommandContractCodec.StatusCommandKind,
            NodeId = NodeId,
            InstallationId = InstallationId,
            View = "status"
        };
        const string intentJson = "{\"schemaVersion\":1,\"kind\":\"inspect-node-status-v1\",\"nodeId\":\"30000000-0000-0000-0000-000000000001\",\"installationId\":\"60000000-0000-0000-0000-000000000001\",\"view\":\"status\"}";
        AssertVector(NodeCommandContractCodec.SerializeIntent(intent), intentJson,
            "701cf4216ccb626d1fe0b667018cf802457a728aa6f5906a5bcc512df29181ae");

        var command = NodeCommandContractCodec.SealCommand(CreateCommand());
        Assert.That(command.RequestSha256, Is.EqualTo(CommandHash));
        const string commandJson = "{\"schemaVersion\":1,\"commandId\":\"11111111-1111-1111-1111-111111111111\",\"clientRequestId\":\"22222222-2222-2222-2222-222222222222\",\"kind\":\"inspect-node-status-v1\",\"target\":{\"scope\":\"node-management\",\"nodeId\":\"30000000-0000-0000-0000-000000000001\",\"installationId\":\"60000000-0000-0000-0000-000000000001\",\"controlPlaneRealmId\":\"40000000-0000-0000-0000-000000000001\",\"recoveryEpoch\":\"3\",\"incarnation\":\"1\",\"credentialRevision\":\"1\"},\"issuedAtUtc\":\"2026-10-14T12:00:00.0000000Z\",\"deadlineUtc\":\"2026-10-14T12:05:00.0000000Z\",\"requestSha256\":\"963dc18e67b9f72ad794c5687994961c3a669719aff09ef2158be3ac8594d181\"}";
        var commandPreimage = commandJson[..commandJson.LastIndexOf(",\"requestSha256\"", StringComparison.Ordinal)] + "}";
        AssertExactJson(NodeCommandContractCodec.SerializeCommand(command), commandJson);
        AssertHash(commandPreimage, command.RequestSha256, CommandHash);

        var result = NodeCommandContractCodec.SealResult(CreateResult());
        Assert.That(result.ResultSha256, Is.EqualTo(ResultHash));
        const string resultJson = "{\"schemaVersion\":1,\"commandId\":\"11111111-1111-1111-1111-111111111111\",\"requestSha256\":\"963dc18e67b9f72ad794c5687994961c3a669719aff09ef2158be3ac8594d181\",\"nodeId\":\"30000000-0000-0000-0000-000000000001\",\"installationId\":\"60000000-0000-0000-0000-000000000001\",\"controlPlaneRealmId\":\"40000000-0000-0000-0000-000000000001\",\"recoveryEpoch\":\"3\",\"originalIncarnation\":\"1\",\"credentialRevision\":\"1\",\"reportingIncarnation\":\"1\",\"observedAtUtc\":\"2026-10-14T12:00:01.0000000Z\",\"startupPhase\":\"RecoveryRequired\",\"activationReceiptSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"checkpointSha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"resultSha256\":\"e0cb44b49ee3f83e29475d1ed3345a9da7cb266b9a568d3856d3ca53999400ee\"}";
        var resultPreimage = resultJson[..resultJson.LastIndexOf(",\"resultSha256\"", StringComparison.Ordinal)] + "}";
        AssertExactJson(NodeCommandContractCodec.SerializeResult(result), resultJson);
        AssertHash(resultPreimage, result.ResultSha256, ResultHash);

        var receipt = NodeCommandContractCodec.SealReceipt(CreateReceipt(result));
        Assert.That(receipt.ReceiptSha256, Is.EqualTo(ReceiptHash));
        const string receiptJson = "{\"schemaVersion\":1,\"receiptId\":\"77777777-7777-7777-7777-777777777777\",\"commandId\":\"11111111-1111-1111-1111-111111111111\",\"nodeId\":\"30000000-0000-0000-0000-000000000001\",\"installationId\":\"60000000-0000-0000-0000-000000000001\",\"controlPlaneRealmId\":\"40000000-0000-0000-0000-000000000001\",\"recoveryEpoch\":\"3\",\"credentialRevision\":\"1\",\"originalIncarnation\":\"1\",\"reportingIncarnation\":\"1\",\"resultSha256\":\"e0cb44b49ee3f83e29475d1ed3345a9da7cb266b9a568d3856d3ca53999400ee\",\"status\":\"ResultStored\",\"receivedAtUtc\":\"2026-10-14T12:00:02.0000000Z\",\"storedAtUtc\":\"2026-10-14T12:00:02.1000000Z\",\"receiptSha256\":\"dee912a14ce5f28d7f0c967980a741614082d05966f51279f46329b732b9520e\"}";
        var receiptPreimage = receiptJson[..receiptJson.LastIndexOf(",\"receiptSha256\"", StringComparison.Ordinal)] + "}";
        AssertExactJson(NodeCommandContractCodec.SerializeReceipt(receipt), receiptJson);
        AssertHash(receiptPreimage, receipt.ReceiptSha256, ReceiptHash);
    }

    [Test]
    public void Canonical_parsers_round_trip_and_reject_noncanonical_or_malformed_documents()
    {
        var command = NodeCommandContractCodec.SealCommand(CreateCommand());
        var commandBytes = NodeCommandContractCodec.SerializeCommand(command);
        Assert.That(NodeCommandContractCodec.SerializeCommand(NodeCommandContractCodec.ParseCommand(commandBytes)),
            Is.EqualTo(commandBytes));
        Assert.That(() => NodeCommandContractCodec.ParseCommand(InsertAfterPrefix(commandBytes, "{\"schemaVersion\":1,",
            "\"schemaVersion\":1,\"extra\":0,")), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.ParseCommand(Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(commandBytes).Replace("\"kind\":\"inspect-node-status-v1\"",
                "\"kind\":\"inspect-node-status-v1\",\"kind\":\"inspect-node-status-v1\"", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.ParseCommand(Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(commandBytes).Replace("\"incarnation\":\"1\"",
                "\"incarnation\":\"1\",\"incarnation\":\"1\"", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "nested duplicate properties are rejected");
        Assert.That(() => NodeCommandContractCodec.ParseCommand([.. commandBytes, (byte)' ']),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.ParseCommand([.. commandBytes, (byte)'x']),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.ParseCommand(new byte[NodeCommandContractCodec.MaximumDocumentBytes + 1]),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.ParseCommand(Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(commandBytes).Replace("\"requestSha256\":\"" + CommandHash,
                "\"requestSha256\":\"" + new string('0', 64), StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.SealCommand(command with { SchemaVersion = 2 }),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.SealCommand(command with { Kind = "execute-workflow" }),
            Throws.TypeOf<InvalidDataException>());
        foreach (var invalidInteger in new[] { "0", "01", "-1", "9223372036854775808" })
            Assert.That(() => NodeCommandContractCodec.SealCommand(command with
            {
                Target = command.Target with { Incarnation = invalidInteger }
            }), Throws.TypeOf<InvalidDataException>(), invalidInteger);

        var result = NodeCommandContractCodec.SealResult(CreateResult());
        var resultBytes = NodeCommandContractCodec.SerializeResult(result);
        Assert.That(NodeCommandContractCodec.SerializeResult(NodeCommandContractCodec.ParseResult(resultBytes)), Is.EqualTo(resultBytes));
        var resultJson = Encoding.UTF8.GetString(resultBytes);
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(
            resultJson.Replace("\"startupPhase\":\"RecoveryRequired\"",
                "\"startupPhase\":\"RecoveryRequired\",\"startupPhase\":\"RecoveryRequired\"", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "duplicate result property");
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(
            resultJson.Insert(resultJson.Length - 1, ",\"unknown\":0"))), Throws.TypeOf<InvalidDataException>(),
            "unknown result property");
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(
            resultJson.Replace("{\"schemaVersion\":1,\"commandId\":\"" + CommandId.ToString("D") + "\"",
                "{\"commandId\":\"" + CommandId.ToString("D") + "\",\"schemaVersion\":1", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(),
            "wrong result property order");
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(resultJson.Replace(",\"commandId\"", ", \"commandId\"", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "noncanonical result whitespace");
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(
            resultJson.Replace("2026-10-14T12:00:01.0000000Z", "2026-10-14T12:00:01+00:00", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "noncanonical result timestamp");
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(
            resultJson.Replace("\"resultSha256\":\"" + ResultHash,
                "\"resultSha256\":\"" + new string('0', 64), StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "result seal mismatch");
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(
            resultJson.Replace("\"startupPhase\":\"RecoveryRequired\"",
                "\"startupPhase\":\"Active\"", StringComparison.Ordinal))), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.ParseResult(Encoding.UTF8.GetBytes(
            resultJson.Replace("\"recoveryEpoch\":\"3\"",
                "\"recoveryEpoch\":3", StringComparison.Ordinal))), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.SealResult(result with { StartupPhase = "Active" }),
            Throws.TypeOf<InvalidDataException>());

        var receipt = NodeCommandContractCodec.SealReceipt(CreateReceipt(result));
        var receiptBytes = NodeCommandContractCodec.SerializeReceipt(receipt);
        Assert.That(NodeCommandContractCodec.SerializeReceipt(NodeCommandContractCodec.ParseReceipt(receiptBytes)),
            Is.EqualTo(receiptBytes));
        var receiptJson = Encoding.UTF8.GetString(receiptBytes);
        Assert.That(() => NodeCommandContractCodec.ParseReceipt(Encoding.UTF8.GetBytes(
            receiptJson.Replace("\"status\":\"ResultStored\"",
                "\"status\":\"ResultStored\",\"status\":\"ResultStored\"", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "duplicate receipt property");
        Assert.That(() => NodeCommandContractCodec.ParseReceipt(Encoding.UTF8.GetBytes(
            receiptJson.Insert(receiptJson.Length - 1, ",\"unknown\":0"))), Throws.TypeOf<InvalidDataException>(),
            "unknown receipt property");
        Assert.That(() => NodeCommandContractCodec.ParseReceipt(Encoding.UTF8.GetBytes(
            receiptJson.Replace("\"status\":\"ResultStored\",\"receivedAtUtc\":\"2026-10-14T12:00:02.0000000Z\"",
                "\"receivedAtUtc\":\"2026-10-14T12:00:02.0000000Z\",\"status\":\"ResultStored\"", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "wrong receipt property order");
        Assert.That(() => NodeCommandContractCodec.ParseReceipt(Encoding.UTF8.GetBytes(
            receiptJson.Replace(",\"commandId\"", ", \"commandId\"", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "noncanonical receipt whitespace");
        Assert.That(() => NodeCommandContractCodec.ParseReceipt(Encoding.UTF8.GetBytes(
            receiptJson.Replace("2026-10-14T12:00:02.0000000Z", "2026-10-14T12:00:02+00:00", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "noncanonical receipt timestamp");
        Assert.That(() => NodeCommandContractCodec.ParseReceipt(Encoding.UTF8.GetBytes(
            receiptJson.Replace("\"receiptSha256\":\"" + ReceiptHash,
                "\"receiptSha256\":\"" + new string('0', 64), StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>(), "receipt seal mismatch");
        Assert.That(() => NodeCommandContractCodec.ParseReceipt(Encoding.UTF8.GetBytes(
            receiptJson.Replace("\"status\":\"ResultStored\"",
                "\"status\":\"Accepted\"", StringComparison.Ordinal))), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.SealReceipt(receipt with { RecoveryEpoch = "0" }),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Intent_key_retry_distinguishes_changed_intent_and_ignores_server_generated_values()
    {
        var original = CreateIntent();
        Assert.That(NodeCommandContractCodec.ComputeIntentSha256(original),
            Is.EqualTo("701cf4216ccb626d1fe0b667018cf802457a728aa6f5906a5bcc512df29181ae"));
        Assert.That(NodeCommandContractCodec.ComputeIntentSha256(original with { NodeId = Guid.NewGuid() }),
            Is.Not.EqualTo(NodeCommandContractCodec.ComputeIntentSha256(original)));
        Assert.That(() => NodeCommandContractCodec.SerializeIntent(original with { View = "logs" }),
            Throws.TypeOf<InvalidDataException>());

        var command = NodeCommandContractCodec.SealCommand(CreateCommand());
        var retried = command with
        {
            CommandId = Guid.NewGuid(),
            IssuedAtUtc = UtcText(IssuedAt.AddSeconds(3)),
            DeadlineUtc = UtcText(Deadline.AddSeconds(3)),
            Target = command.Target with { RecoveryEpoch = "4", Incarnation = "2" }
        };
        Assert.That(NodeCommandContractCodec.ComputeIntentSha256(CreateIntent(retried)),
            Is.EqualTo(NodeCommandContractCodec.ComputeIntentSha256(CreateIntent(command))));
        Assert.That(() => NodeCommandContractCodec.SealCommand(command with
        {
            Target = command.Target with { RecoveryEpoch = "9223372036854775808" }
        }), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Command_and_result_time_bounds_are_exact_and_use_injected_clocks()
    {
        var now = IssuedAt;
        var futureBoundary = NodeCommandContractCodec.SealCommand(CreateCommand() with
        {
            IssuedAtUtc = UtcText(now.AddSeconds(30)),
            DeadlineUtc = UtcText(now.AddSeconds(30).AddMinutes(5))
        });
        Assert.DoesNotThrow(() => NodeCommandContractCodec.ValidateCommandTime(futureBoundary, now));
        Assert.That(() => NodeCommandContractCodec.ValidateCommandTime(futureBoundary, now.AddTicks(-1)),
            Throws.TypeOf<InvalidDataException>());

        var expired = NodeCommandContractCodec.SealCommand(CreateCommand() with { DeadlineUtc = UtcText(now.AddSeconds(5)) });
        Assert.That(() => NodeCommandContractCodec.ValidateCommandTime(expired, now.AddSeconds(6)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.SealCommand(CreateCommand() with
        {
            DeadlineUtc = UtcText(IssuedAt.AddMinutes(5).AddTicks(1))
        }), Throws.TypeOf<InvalidDataException>());

        var command = NodeCommandContractCodec.SealCommand(CreateCommand());
        var binding = CreateBinding();
        var result = NodeCommandContractCodec.SealResult(CreateResult());
        Assert.DoesNotThrow(() => NodeCommandContractCodec.ValidateResultBinding(command, result, binding, IssuedAt.AddSeconds(2)));
        var wrongIncarnation = NodeCommandContractCodec.SealResult(result with { ReportingIncarnation = "2" });
        Assert.That(() => NodeCommandContractCodec.ValidateResultBinding(command, wrongIncarnation, binding,
            IssuedAt.AddSeconds(2)), Throws.TypeOf<InvalidDataException>());
        var tooEarly = NodeCommandContractCodec.SealResult(result with
        {
            ObservedAtUtc = UtcText(IssuedAt.AddSeconds(-30).AddTicks(-1))
        });
        Assert.That(() => NodeCommandContractCodec.ValidateResultBinding(command, tooEarly, binding,
            IssuedAt.AddSeconds(2)), Throws.TypeOf<InvalidDataException>());
        var afterDeadline = NodeCommandContractCodec.SealResult(result with
        {
            ObservedAtUtc = UtcText(Deadline.AddTicks(1))
        });
        Assert.That(() => NodeCommandContractCodec.ValidateResultBinding(command, afterDeadline, binding,
            Deadline.AddSeconds(-1)), Throws.TypeOf<InvalidDataException>());
        var afterServerWindow = NodeCommandContractCodec.SealResult(result with
        {
            ObservedAtUtc = UtcText(IssuedAt.AddSeconds(31))
        });
        Assert.That(() => NodeCommandContractCodec.ValidateResultBinding(command, afterServerWindow, binding,
            IssuedAt), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.ParseCommand(Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(NodeCommandContractCodec.SerializeCommand(command))
                .Replace("2026-10-14T12:00:00.0000000Z", "2026-10-14T12:00:00+00:00", StringComparison.Ordinal))),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Receipt_validation_binds_full_realm_epoch_credential_and_incarnation_tuple()
    {
        var command = NodeCommandContractCodec.SealCommand(CreateCommand());
        var result = NodeCommandContractCodec.SealResult(CreateResult());
        var receipt = NodeCommandContractCodec.SealReceipt(CreateReceipt(result));
        var binding = CreateBinding();
        Assert.DoesNotThrow(() => NodeCommandContractCodec.ValidateReceiptBinding(command, result, receipt, binding));
        var wrongEpoch = NodeCommandContractCodec.SealReceipt(receipt with { RecoveryEpoch = "4" });
        Assert.That(() => NodeCommandContractCodec.ValidateReceiptBinding(command, result, wrongEpoch, binding),
            Throws.TypeOf<InvalidDataException>());
        var wrongCredential = NodeCommandContractCodec.SealReceipt(receipt with { CredentialRevision = "2" });
        Assert.That(() => NodeCommandContractCodec.ValidateReceiptBinding(command, result, wrongCredential, binding),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NodeCommandContractCodec.SealReceipt(receipt with { StoredAtUtc = UtcText(IssuedAt) }),
            Throws.TypeOf<InvalidDataException>());
    }

    private static NodeCommandIntentV1 CreateIntent(NodeStatusCommandV1? command = null) => new()
    {
        SchemaVersion = 1,
        Kind = NodeCommandContractCodec.StatusCommandKind,
        NodeId = command?.Target.NodeId ?? NodeId,
        InstallationId = command?.Target.InstallationId ?? InstallationId,
        View = "status"
    };

    private static NodeStatusCommandV1 CreateCommand() => new()
    {
        SchemaVersion = 1,
        CommandId = CommandId,
        ClientRequestId = ClientRequestId,
        Kind = NodeCommandContractCodec.StatusCommandKind,
        Target = new NodeCommandTargetV1
        {
            Scope = "node-management",
            NodeId = NodeId,
            InstallationId = InstallationId,
            ControlPlaneRealmId = RealmId,
            RecoveryEpoch = "3",
            Incarnation = "1",
            CredentialRevision = "1"
        },
        IssuedAtUtc = UtcText(IssuedAt),
        DeadlineUtc = UtcText(Deadline),
        RequestSha256 = new string('0', 64)
    };

    private static NodeStatusResultV1 CreateResult() => new()
    {
        SchemaVersion = 1,
        CommandId = CommandId,
        RequestSha256 = CommandHash,
        NodeId = NodeId,
        InstallationId = InstallationId,
        ControlPlaneRealmId = RealmId,
        RecoveryEpoch = "3",
        OriginalIncarnation = "1",
        CredentialRevision = "1",
        ReportingIncarnation = "1",
        ObservedAtUtc = UtcText(ObservedAt),
        StartupPhase = "RecoveryRequired",
        ActivationReceiptSha256 = new string('a', 64),
        CheckpointSha256 = new string('b', 64),
        ResultSha256 = new string('0', 64)
    };

    private static NodeCommandResultReceiptV1 CreateReceipt(NodeStatusResultV1 result) => new()
    {
        SchemaVersion = 1,
        ReceiptId = ReceiptId,
        CommandId = CommandId,
        NodeId = NodeId,
        InstallationId = InstallationId,
        ControlPlaneRealmId = RealmId,
        RecoveryEpoch = "3",
        CredentialRevision = "1",
        OriginalIncarnation = "1",
        ReportingIncarnation = "1",
        ResultSha256 = result.ResultSha256,
        Status = "ResultStored",
        ReceivedAtUtc = "2026-10-14T12:00:02.0000000Z",
        StoredAtUtc = "2026-10-14T12:00:02.1000000Z",
        ReceiptSha256 = new string('0', 64)
    };

    private static NodeCommandBinding CreateBinding() => new(NodeId, InstallationId, RealmId, 3, 1, 1);

    private static void AssertVector(byte[] actualBytes, string expectedJson, string expectedSha256)
    {
        AssertExactJson(actualBytes, expectedJson);
        AssertHash(expectedJson, NodeCommandContractCodec.ComputeIntentSha256(CreateIntent()), expectedSha256);
    }

    private static void AssertExactJson(byte[] actualBytes, string expectedJson) =>
        Assert.That(actualBytes, Is.EqualTo(Encoding.UTF8.GetBytes(expectedJson)));

    private static void AssertHash(string preimage, string actualHash, string expectedHash)
    {
        var bytes = Encoding.UTF8.GetBytes(preimage);
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Is.EqualTo(expectedHash));
        Assert.That(actualHash, Is.EqualTo(expectedHash));
    }

    private static byte[] InsertAfterPrefix(byte[] bytes, string prefix, string replacement) =>
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(prefix, replacement, StringComparison.Ordinal));

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static string UtcText(DateTimeOffset value) => value.ToUniversalTime().ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
