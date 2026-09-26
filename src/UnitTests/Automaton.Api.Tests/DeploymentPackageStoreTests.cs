using Automaton.Api.Config;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class DeploymentPackageStoreTests
{
    [Test]
    public async Task StoreAsync_RejectsPackageWhenAggregateByteLimitWouldBeExceeded()
    {
        var root = CreateRoot();
        var directory = Path.Combine(root, "AppData", "deployment-operations");
        Directory.CreateDirectory(directory);
        var existingPath = Path.Combine(directory, "existing.zip");
        await File.WriteAllBytesAsync(existingPath, [1, 2, 3, 4]);
        var store = CreateStore(root, maximumBytes: 6, maximumPackages: 10);

        try
        {
            var exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.StoreAsync(Guid.NewGuid(), CreateFile([5, 6, 7]), CancellationToken.None));

            Assert.That(exception!.Message, Does.Contain("storage is full"));
            Assert.That(File.ReadAllBytes(existingPath), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task StoreAsync_RejectsPackageWhenStoredFileCountLimitIsReached()
    {
        var root = CreateRoot();
        var directory = Path.Combine(root, "AppData", "deployment-operations");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "existing.zip"), [1]);
        var store = CreateStore(root, maximumBytes: 100, maximumPackages: 1);

        try
        {
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.StoreAsync(Guid.NewGuid(), CreateFile([2]), CancellationToken.None));

            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task StoreAsync_UsesStagingAndRemovesPartialFileAfterCopyFailure()
    {
        var root = CreateRoot();
        var directory = Path.Combine(root, "AppData", "deployment-operations");
        var store = CreateStore(root, maximumBytes: 100, maximumPackages: 2);
        using var stream = new MemoryStream([1, 2]);
        var file = new FormFile(stream, 0, 3, "file", "test.zip");

        try
        {
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.StoreAsync(Guid.NewGuid(), file, CancellationToken.None));

            Assert.That(Directory.Exists(directory), Is.True);
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task StoreAsync_StoresPackageWithinBothLimits()
    {
        var root = CreateRoot();
        var store = CreateStore(root, maximumBytes: 10, maximumPackages: 2);
        var content = new byte[] { 1, 2, 3 };

        try
        {
            var packagePath = await store.StoreAsync(Guid.NewGuid(), CreateFile(content), CancellationToken.None);

            Assert.That(File.ReadAllBytes(packagePath), Is.EqualTo(content));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(packagePath)!), Has.Length.EqualTo(1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CleanupUnreferencedPackagesAsync_DeletesOldOrphansAndPreservesActiveOrRecentFiles()
    {
        var root = CreateRoot();
        var directory = Path.Combine(root, "AppData", "deployment-operations");
        Directory.CreateDirectory(directory);
        var oldOrphan = Path.Combine(directory, "orphan.zip");
        var oldActive = Path.Combine(directory, "active.zip");
        var recentOrphan = Path.Combine(directory, "recent.zip");
        await File.WriteAllBytesAsync(oldOrphan, [1]);
        await File.WriteAllBytesAsync(oldActive, [2]);
        await File.WriteAllBytesAsync(recentOrphan, [3]);
        var oldTimestamp = DateTime.UtcNow.AddDays(-2);
        File.SetLastWriteTimeUtc(oldOrphan, oldTimestamp);
        File.SetLastWriteTimeUtc(oldActive, oldTimestamp);
        var store = CreateStore(root, maximumBytes: 10, maximumPackages: 10);

        try
        {
            var deletedCount = await store.CleanupUnreferencedPackagesAsync(
                [oldActive], TimeSpan.FromDays(1), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(deletedCount, Is.EqualTo(1));
                Assert.That(File.Exists(oldOrphan), Is.False);
                Assert.That(File.Exists(oldActive), Is.True);
                Assert.That(File.Exists(recentOrphan), Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-deployment-package-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static DeploymentPackageStore CreateStore(string root, long maximumBytes, int maximumPackages) =>
        new(
            new TestWebHostEnvironment { ContentRootPath = root },
            Options.Create(new DeploymentPackageStorageOptions
            {
                MaximumStoredBytes = maximumBytes,
                MaximumStoredPackages = maximumPackages
            }));

    private static IFormFile CreateFile(byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", "test.zip");
}
