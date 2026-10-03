using Automaton.Api.Config;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

public sealed class RunnerFileTransferStorageTests
{
    private string root = null!;

    [SetUp]
    public void SetUp() => root = Path.Combine(Path.GetTempPath(), "automaton-transfer-store-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task RunnerArtifactIsStoredAtTheOperationIdentityAndCanBeReopened()
    {
        var store = CreateStore();
        var operationId = Guid.NewGuid();
        var expected = new byte[] { 1, 2, 3, 4 };
        await using var source = new MemoryStream(expected);

        var path = await store.StoreRunnerTransferAsync(operationId, input: true, source, expected.Length, CancellationToken.None);
        await using var saved = store.OpenRunnerTransfer(operationId, input: true);

        var actual = new byte[checked((int)saved.Length)];
        await saved.ReadExactlyAsync(actual);
        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(store.GetRunnerTransferPath(operationId, input: true)));
            Assert.That(actual, Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task StoreWaitsForTheSharedAppDataCapacityLock()
    {
        var appData = Path.Combine(root, "AppData");
        Directory.CreateDirectory(appData);
        var lockPath = Path.Combine(appData, ".deployment-operations.lock");
        using var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var source = new MemoryStream([5, 6, 7]);

        var write = CreateStore().StoreRunnerTransferAsync(Guid.NewGuid(), input: true,
            source, 3, timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token);
        Assert.That(write.IsCompleted, Is.False);
        heldLock.Dispose();
        var path = await write;

        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void RunnerArtifactAboveBoundIsRejectedBeforeReadingTheBody()
    {
        var store = CreateStore();
        using var source = new MemoryStream();

        Assert.ThrowsAsync<InvalidDataException>(() => store.StoreRunnerTransferAsync(Guid.NewGuid(),
            input: false, source, RunnerFileTransferBounds.MaximumBytes + 1, CancellationToken.None));
        Assert.That(Directory.Exists(Path.Combine(root, "AppData", "deployment-operations")), Is.False);
    }

    private DeploymentPackageStore CreateStore()
        => new(new TestWebHostEnvironment { ContentRootPath = root },
            Options.Create(new DeploymentPackageStorageOptions()));
}
