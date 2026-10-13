using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Node.Activation;
using Automaton.Node.Preparation;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeActivationClientTests
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [TestCase("http://node.example")]
    [TestCase("https://node.example/path")]
    [TestCase("https://user@node.example")]
    [TestCase("https://node.example/?query=1")]
    [TestCase("https://node.example/#fragment")]
    public void ManagementOriginRejectsAnythingButAnHttpsOrigin(string value)
    {
        Assert.That(NodeActivationClient.TryCreateManagementOrigin(value, out _), Is.False);
    }

    [Test]
    public void ManagementOriginAcceptsHttpsOrigin()
    {
        Assert.That(NodeActivationClient.TryCreateManagementOrigin("https://node.example", out var origin), Is.True);
        Assert.That(origin!.AbsoluteUri, Is.EqualTo("https://node.example/"));
    }

    [Test]
    public async Task ActivationPersistsUnknownBeforeTransportAndRetriesIdenticalBytes()
    {
        var setup = CreateClientSetup();
        using var activationClient = new NodeActivationClient(setup.Handler, new Uri("https://node.example"), setup.IntentLedger);

        var result = await activationClient.SendActivationAsync(setup.NodeIdentity, setup.AuthorityEvidence, setup.Intent,
            [setup.Intent], setup.PreflightReadback,
            "synthetic-api-key", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(setup.ExpectedReceipt));
            Assert.That(setup.Handler.RequestBodies, Has.Count.EqualTo(2));
            Assert.That(setup.Handler.RequestBodies[1], Is.EqualTo(setup.Handler.RequestBodies[0]));
            Assert.That(setup.IntentLedger.Events[0], Is.EqualTo("ResponseUnknown"));
            Assert.That(setup.IntentLedger.Events[^1], Is.EqualTo("ReceiptPersisted"));
            Assert.That(setup.IntentLedger.PersistedReceipt, Is.EqualTo(setup.ReceiptBytes));
        });
    }

    [Test]
    public async Task LostResponseReadbackPersistsTheExactReceiptForItsDurableIntent()
    {
        var setup = CreateClientSetup(intentState: "ResponseUnknown", ledgerAlreadyUnknown: true);
        var readbackBytes = CreateWinningReadback(setup);
        using var activationClient = new NodeActivationClient(new FixedHandler(readbackBytes), new Uri("https://node.example"), setup.IntentLedger);

        var snapshot = await activationClient.ReadCurrentAsync(setup.NodeIdentity, setup.AuthorityEvidence,
            [setup.Intent], "synthetic-api-key", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Readback.CurrentActivationRequestId, Is.EqualTo(setup.Intent.ActivationRequestId));
            Assert.That(snapshot.ExactResponseJson, Is.EqualTo(readbackBytes));
            Assert.That(setup.IntentLedger.Events[^1], Is.EqualTo("ReceiptPersisted"));
            Assert.That(setup.IntentLedger.PersistedReceipt, Is.EqualTo(setup.ReceiptBytes));
        });
    }

    private static ClientSetup CreateClientSetup(string intentState = "Prepared", bool ledgerAlreadyUnknown = false)
    {
        var identity = Identity();
        var authority = Authority();
        var recoveryDigest = new string('c', 64);
        var request = new NodeActivationRequestDocument(1, identity.ControlPlaneRealmId, authority.RecoveryEpoch,
            identity.NodeId, identity.InstallationId, Guid.NewGuid(), 0, 1, new string('a', 64), recoveryDigest, [1], null);
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, CanonicalOptions);
        var intent = new NodeActivationIntent(request.ActivationRequestId, Guid.NewGuid(), "0", "3",
            Digest(requestBytes), Convert.ToBase64String(requestBytes), intentState, null, null);
        var receipt = new NodeActivationReceipt(1, identity.NodeId, identity.InstallationId, identity.ControlPlaneRealmId,
            intent.ActivationRequestId, 1, 3, recoveryDigest, "Recovering",
            new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));
        var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(receipt, ResponseOptions());
        var ledger = new FakeIntentLedger(intent, ledgerAlreadyUnknown);
        var noActivation = new NodeActivationCurrentReadback(1, identity.ControlPlaneRealmId, identity.NodeId,
            identity.InstallationId, "1", null, "3", "NoActivation", null, null, null, null);
        var readbackBytes = JsonSerializer.SerializeToUtf8Bytes(noActivation, ResponseOptions());
        var readbackSnapshot = new NodeActivationReadbackSnapshot(noActivation, readbackBytes, Digest(readbackBytes));
        return new ClientSetup(identity, authority, intent, receipt, receiptBytes, ledger, readbackSnapshot,
            new RetryingHandler(receiptBytes, ledger));
    }

    [Test]
    public void ReadbackParserRejectsDuplicateAndUnknownProperties()
    {
        var valid = "{\"schemaVersion\":1,\"controlPlaneRealmId\":\"40000000-0000-0000-0000-000000000001\",\"nodeId\":\"30000000-0000-0000-0000-000000000001\",\"installationId\":\"60000000-0000-0000-0000-000000000001\",\"credentialRevision\":\"1\",\"recoveryEpoch\":null,\"authoritativeRecoveryEpoch\":\"3\",\"status\":\"NoActivation\",\"currentIncarnation\":null,\"currentActivationRequestId\":null,\"requestSha256\":null,\"activationReceiptJsonBase64\":null}";
        Assert.That(NodeActivationContractParser.ParseReadback(Encoding.UTF8.GetBytes(valid)).Status, Is.EqualTo("NoActivation"));
        Assert.That(() => NodeActivationContractParser.ParseReadback(Encoding.UTF8.GetBytes(valid.Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"schemaVersion\":1,"))),
            Throws.InstanceOf<InvalidDataException>());
        Assert.That(() => NodeActivationContractParser.ParseReadback(Encoding.UTF8.GetBytes(valid.Replace("{", "{\"extra\":true,"))),
            Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void RecoveryViewAndGenesisCheckpointBindExactCanonicalEvidence()
    {
        var identity = Identity();
        var digest = Digest(Encoding.UTF8.GetBytes("{}"));
        var view = NodeRecoveryView.Create(identity, 3, null, digest, digest, new string('d', 64));
        var checkpoint = NodeActivationCheckpoint.CreateGenesis(identity, 3, view);
        var serialized = checkpoint.SerializeAndValidate();
        var parsed = NodeActivationCheckpoint.Parse(serialized);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.RecoveryCheckpointSha256, Is.EqualTo(view.ComputeDigest()));
            Assert.That(parsed.RecoveryView, Is.EqualTo(view));
            Assert.That(parsed.CurrentIncarnation, Is.Null);
            Assert.That(parsed.ActivationIntents, Is.Empty);
            Assert.That(parsed.CheckpointSequence, Is.EqualTo("1"));
        });
        Assert.That(() => (checkpoint with { RecoveryCheckpointSha256 = new string('e', 64) }).SerializeAndValidate(),
            Throws.InstanceOf<NodeProtectedStateException>());
    }

    private static NodeIdentityManifest Identity() => new(1,
        Guid.Parse("40000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        Guid.Parse("60000000-0000-0000-0000-000000000001"), "1", new string('f', 64),
        Guid.Parse("80000000-0000-0000-0000-000000000001"), new string('a', 64), "node-client-key.pem", "node-api-key");

    private static NodeExternalAuthorityEvidence Authority() => new(
        Guid.Parse("40000000-0000-0000-0000-000000000001"), 3, "lab-revision-1", new string('b', 64));

    private static string Digest(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonSerializerOptions ResponseOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static byte[] CreateWinningReadback(ClientSetup setup)
    {
        var response = new NodeActivationCurrentReadback(1, setup.NodeIdentity.ControlPlaneRealmId,
            setup.NodeIdentity.NodeId, setup.NodeIdentity.InstallationId, "1", "3", "3", "Recovering", "1",
            setup.Intent.ActivationRequestId, setup.Intent.RequestSha256, Convert.ToBase64String(setup.ReceiptBytes));
        return JsonSerializer.SerializeToUtf8Bytes(response, ResponseOptions());
    }

    private sealed class FakeIntentLedger(NodeActivationIntent initial, bool alreadyUnknown = false) : INodeActivationIntentLedger
    {
        public List<string> Events { get; } = alreadyUnknown ? ["ResponseUnknown"] : [];
        public byte[]? PersistedReceipt { get; private set; }

        public NodeActivationIntent MarkResponseUnknown(Guid requestId)
        {
            Assert.That(Events, Is.Empty);
            Assert.That(requestId, Is.EqualTo(initial.ActivationRequestId));
            Events.Add("ResponseUnknown");
            return initial with { State = "ResponseUnknown" };
        }

        public NodeActivationIntent GetIntent(Guid requestId)
        {
            Assert.That(requestId, Is.EqualTo(initial.ActivationRequestId));
            return initial with { State = "ResponseUnknown" };
        }

        public NodeActivationIntent PersistReceipt(Guid requestId, NodeActivationReceipt receipt, ReadOnlySpan<byte> exactReceipt)
        {
            Assert.That(Events, Is.EqualTo(new[] { "ResponseUnknown" }));
            PersistedReceipt = exactReceipt.ToArray();
            Events.Add("ReceiptPersisted");
            return initial with { State = "ReceiptPersisted", ReceiptJsonBase64 = Convert.ToBase64String(exactReceipt),
                ReceiptSha256 = Digest(exactReceipt.ToArray()) };
        }
    }

    private sealed class RetryingHandler(byte[] receipt, FakeIntentLedger ledger) : HttpMessageHandler
    {
        public List<byte[]> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(ledger.Events.FirstOrDefault(), Is.EqualTo("ResponseUnknown"));
            RequestBodies.Add(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            return RequestBodies.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(receipt)
                };
        }
    }

    private sealed class FixedHandler(byte[] responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(responseBody)
            });
    }

    private sealed record ClientSetup(NodeIdentityManifest NodeIdentity, NodeExternalAuthorityEvidence AuthorityEvidence,
        NodeActivationIntent Intent, NodeActivationReceipt ExpectedReceipt, byte[] ReceiptBytes, FakeIntentLedger IntentLedger,
        NodeActivationReadbackSnapshot PreflightReadback, RetryingHandler Handler);
}
