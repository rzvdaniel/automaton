using System.Text.Json;
using Automaton.Node.Activation;
using Automaton.Node.Preparation;
using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeLocalStateStoreRecoveryFailureTests
{
    [Test]
    public void Receipt_reconciliation_is_durable_and_survives_lock_reopen()
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        var statePath = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        try
        {
            NodeIdentityManifest identity;
            NodeActivationIntent intent;
            byte[] exactReceipt;
            using (var installationLock = NodeLocalStateStoreIntegrationTests.Acquire(statePath))
            {
                var store = new NodeLocalStateStore(installationLock);
                identity = NodeLocalStateStoreIntegrationTests.InstallIdentityAndGenesis(store);
                var checkpoint = store.ReadCheckpoint()!;
                intent = NodeLocalStateStoreIntegrationTests.CreateIntent(store, identity, checkpoint,
                    Guid.NewGuid(), Guid.NewGuid());
                intent = store.MarkResponseUnknown(intent.ActivationRequestId);
                var receipt = new NodeActivationReceipt(1, identity.NodeId, identity.InstallationId,
                    identity.ControlPlaneRealmId, intent.ActivationRequestId, 1, 3,
                    checkpoint.RecoveryCheckpointSha256, "Recovering",
                    new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc));
                exactReceipt = JsonSerializer.SerializeToUtf8Bytes(receipt,
                    NodeLocalStateStore.CanonicalJsonOptions);

                var authority = new NodeExternalAuthorityEvidence(identity.ControlPlaneRealmId, 3,
                    "lab-revision-1", new string('a', 64));
                var readback = new NodeActivationCurrentReadback(1, identity.ControlPlaneRealmId,
                    identity.NodeId, identity.InstallationId, identity.CredentialRevision, "3", "3",
                    "Recovering", "1", intent.ActivationRequestId, intent.RequestSha256,
                    Convert.ToBase64String(exactReceipt));
                NodeActivationContractParser.ValidateReadbackBinding(readback, identity, authority, [intent]);
                var persisted = store.PersistReceipt(intent.ActivationRequestId, receipt, exactReceipt);
                Assert.That(persisted.State, Is.EqualTo("ReceiptPersisted"));
            }

            using var reopenedLock = NodeLocalStateStoreIntegrationTests.Acquire(statePath);
            var reopenedStore = new NodeLocalStateStore(reopenedLock);
            var recovered = reopenedStore.GetIntent(intent.ActivationRequestId);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.State, Is.EqualTo("ReceiptPersisted"));
                Assert.That(Convert.FromBase64String(recovered.ReceiptJsonBase64!), Is.EqualTo(exactReceipt));
                Assert.That(reopenedStore.ReadCheckpoint()!.LastActivationRequestId, Is.EqualTo(intent.ActivationRequestId));
            });
        }
        finally
        {
            Directory.Delete(statePath, recursive: true);
        }
    }

    [TestCase(nameof(NodeProtectedStateWriteStage.FileFsync), false)]
    [TestCase(nameof(NodeProtectedStateWriteStage.Rename), false)]
    [TestCase(nameof(NodeProtectedStateWriteStage.DirectoryFsync), true)]
    public void Checkpoint_write_failures_leave_only_a_valid_old_or_new_checkpoint(
        string failedStage, bool renameAlreadyVisible)
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        var statePath = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        var shouldFail = false;
        try
        {
            var installationLock = new NodeInstallationLock(statePath, (name, stage) =>
            {
                if (shouldFail && name == "activation-checkpoint.json" && stage.ToString() == failedStage)
                    throw new NodeProtectedStateException();
            });
            installationLock.Acquire(CancellationToken.None);
            using (installationLock)
            {
                var store = new NodeLocalStateStore(installationLock);
                var identity = NodeLocalStateStoreIntegrationTests.InstallIdentityAndGenesis(store);
                var before = store.ReadCheckpoint()!;
                shouldFail = true;
                Assert.Throws<NodeProtectedStateException>(() => NodeLocalStateStoreIntegrationTests.CreateIntent(
                    store, identity, before, Guid.NewGuid(), Guid.NewGuid()));
                shouldFail = false;

                var after = store.ReadCheckpoint()!;
                Assert.That(after.ActivationIntents, Has.Count.EqualTo(renameAlreadyVisible ? 1 : 0));
                var sequence = long.Parse(before.CheckpointSequence, System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(after.CheckpointSequence,
                    Is.EqualTo((sequence + (renameAlreadyVisible ? 1 : 0)).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                _ = after.SerializeAndValidate();
            }

            using var reopenedLock = NodeLocalStateStoreIntegrationTests.Acquire(statePath);
            _ = new NodeLocalStateStore(reopenedLock).ReadSnapshot().Checkpoint!.SerializeAndValidate();
        }
        finally
        {
            Directory.Delete(statePath, recursive: true);
        }
    }
}
