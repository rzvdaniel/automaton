using NUnit.Framework;

namespace Automaton.Node.Tests;

[TestFixture]
public sealed class NodeInstallationLockProcessTests
{
    [Test]
    public async Task NodeHostLock_IsExclusiveAcrossIndependentProcesses_AndCrashRecoveryKeepsInode()
    {
        NodeInstallationLockTestEnvironment.RequireSupportedIntegrationEnvironment();
        var stateDirectory = NodeInstallationLockTestEnvironment.CreatePrivateTemporaryDirectory();
        var lockPath = Path.Combine(stateDirectory, "node-manager.lock");
        var processes = new NodeProcessFixture();
        try
        {
            await processes.StartFirstAsync(stateDirectory);
            var contenderError = await processes.VerifyContenderRejectedAsync(stateDirectory);
            Assert.That(contenderError, Does.Contain("AlreadyHeld"));
            await processes.KillFirstAndRestartAsync(stateDirectory, lockPath);
        }
        finally
        {
            processes.Dispose();
            Directory.Delete(stateDirectory, recursive: true);
        }
    }
}
