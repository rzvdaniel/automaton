using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Automaton.Node.Activation;
using Automaton.Node.Preparation;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeLocalStateStoreIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Test]
    public void Protected_identity_and_checkpoint_are_written_and_read_through_the_held_directory_handle()
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        var statePath = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        try
        {
            using var installationLock = Acquire(statePath);
            var store = new NodeLocalStateStore(installationLock);
            var identity = InstallIdentityAndGenesis(store);
            var snapshot = store.ReadSnapshot();
            var files = new[] { "node-identity.json", "node-client-cert.der", "node-client-key.pem", "node-api-key", "activation-checkpoint.json" };

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Identity, Is.EqualTo(identity));
                Assert.That(snapshot.Checkpoint, Is.Not.Null);
                foreach (var file in files)
                {
                    Assert.That(File.Exists(Path.Combine(statePath, file)), Is.True, file);
                    Assert.That(GetUnixFileMode(Path.Combine(statePath, file)),
                        Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), file);
                }
            });
        }
        finally
        {
            Directory.Delete(statePath, recursive: true);
        }
    }

    [Test]
    public void Three_process_starts_preserve_each_potentially_sent_intent_across_crash_boundaries()
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        var statePath = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        try
        {
            NodeIdentityManifest identity;
            NodeActivationCheckpoint checkpoint;
            using (var firstLock = Acquire(statePath))
            {
                var firstStore = new NodeLocalStateStore(firstLock);
                identity = InstallIdentityAndGenesis(firstStore);
                checkpoint = firstStore.ReadCheckpoint()!;
                var first = CreateIntent(firstStore, identity, checkpoint, Guid.NewGuid(), Guid.NewGuid());
                Assert.That(firstStore.MarkResponseUnknown(first.ActivationRequestId).State, Is.EqualTo("ResponseUnknown"));
            }

            using (var secondLock = Acquire(statePath))
            {
                var secondStore = new NodeLocalStateStore(secondLock);
                var afterFirstCrash = secondStore.ReadSnapshot();
                AssertIntent(afterFirstCrash.Checkpoint!, "ResponseUnknown", 1);
                var second = CreateIntent(secondStore, identity, afterFirstCrash.Checkpoint!, Guid.NewGuid(), Guid.NewGuid());
                Assert.That(second.State, Is.EqualTo("Prepared"));
            }

            using var thirdLock = Acquire(statePath);
            var thirdStore = new NodeLocalStateStore(thirdLock);
            var afterSecondCrash = thirdStore.ReadSnapshot();
            var third = CreateIntent(thirdStore, identity, afterSecondCrash.Checkpoint!, Guid.NewGuid(), Guid.NewGuid());
            var finalCheckpoint = thirdStore.ReadCheckpoint()!;

            Assert.Multiple(() =>
            {
                Assert.That(finalCheckpoint.ActivationIntents, Has.Count.EqualTo(3));
                Assert.That(finalCheckpoint.ActivationIntents.Select(value => value.State),
                    Is.EqualTo(new[] { "ResponseUnknown", "AbandonedUnsent", "Prepared" }));
                Assert.That(finalCheckpoint.ActivationIntents.Select(value => value.ActivationRequestId),
                    Is.EqualTo(new[] { finalCheckpoint.ActivationIntents[0].ActivationRequestId,
                        finalCheckpoint.ActivationIntents[1].ActivationRequestId, third.ActivationRequestId }));
                Assert.That(finalCheckpoint.ActivationIntents.Take(2).All(value =>
                    Convert.FromBase64String(value.RequestJsonBase64).Length > 0 &&
                    NodeActivationContractParser.DigestMatches(Convert.FromBase64String(value.RequestJsonBase64), value.RequestSha256)), Is.True);
            });
        }
        finally
        {
            Directory.Delete(statePath, recursive: true);
        }
    }

    internal static NodeInstallationLock Acquire(string statePath)
    {
        var installationLock = new NodeInstallationLock(statePath);
        installationLock.Acquire(CancellationToken.None);
        return installationLock;
    }

    internal static NodeIdentityManifest InstallIdentityAndGenesis(NodeLocalStateStore store)
    {
        var now = DateTimeOffset.UtcNow;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=synthetic-local-state-node", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(now.AddMinutes(-1), now.AddDays(10));
        var certificateDer = certificate.RawData;
        var nodeId = Guid.NewGuid();
        var installationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var certificateDigest = Digest(certificateDer);
        var enrollmentReceipt = new NodeEnrollmentReceiptEvidence(1, nodeId, installationId, realmId, certificateDigest);
        var enrollmentReceiptJson = JsonSerializer.SerializeToUtf8Bytes(enrollmentReceipt, JsonOptions);
        var bundle = new NodeVerifiedCredentialBundle(nodeId, installationId, realmId, 1, Guid.NewGuid(), certificateDer,
            key.ExportPkcs8PrivateKeyPem(), "synthetic-api-key", enrollmentReceipt, enrollmentReceiptJson);
        var identity = store.InstallVerifiedBundle(bundle);
        var authority = new NodeExternalAuthorityEvidence(realmId, 3, "lab-revision-1", new string('a', 64));
        var noActivation = new NodeActivationCurrentReadback(1, realmId, nodeId, installationId, "1", null, "3",
            "NoActivation", null, null, null, null);
        var response = JsonSerializer.SerializeToUtf8Bytes(noActivation, JsonOptions);
        var readback = new NodeActivationReadbackSnapshot(noActivation, response, Digest(response));
        _ = store.InitializeGenesis(readback, authority);
        return identity;
    }

    internal static NodeActivationIntent CreateIntent(NodeLocalStateStore store, NodeIdentityManifest identity,
        NodeActivationCheckpoint checkpoint, Guid processStartId, Guid requestId)
    {
        var request = new NodeActivationRequestDocument(1, identity.ControlPlaneRealmId, 3, identity.NodeId,
            identity.InstallationId, requestId, 0, 1, new string('b', 64), checkpoint.RecoveryCheckpointSha256, [1], null);
        return store.PrepareIntent(processStartId, requestId, 0, 3,
            JsonSerializer.SerializeToUtf8Bytes(request, RequestOptions));
    }

    private static void AssertIntent(NodeActivationCheckpoint checkpoint, string state, int count) =>
        Assert.That(checkpoint.ActivationIntents.Count(value => value.State == state), Is.EqualTo(count));

    private static UnixFileMode GetUnixFileMode(string path)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();
        return File.GetUnixFileMode(path);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
