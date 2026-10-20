using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppSourceCopySymlinkTests
{
    [Test]
    public async Task CopySourceFilesAsync_DoesNotFollowFileOrDirectoryLinksOutsideSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        var sourceUserId = Guid.NewGuid();
        var sourceAppId = Guid.NewGuid();
        var destinationUserId = Guid.NewGuid();
        var destinationAppId = Guid.NewGuid();
        var outsideDirectory = Path.Combine(root, "outside");
        Directory.CreateDirectory(outsideDirectory);
        var sentinel = Path.Combine(outsideDirectory, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "private host data");

        try
        {
            var service = AppFileStorageServiceTests.CreateStorageService(root);
            var source = service.GetAppSourceDirectoryPath(sourceUserId, sourceAppId);
            Directory.CreateDirectory(Path.Combine(source, "backend"));
            try
            {
                File.CreateSymbolicLink(Path.Combine(source, "backend", "linked-sentinel.txt"), sentinel);
                Directory.CreateSymbolicLink(Path.Combine(source, "backend", "linked-private"), outsideDirectory);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                Assert.Ignore($"This environment does not allow symbolic links: {exception.Message}");
            }

            await service.CopySourceFilesAsync(sourceUserId, sourceAppId, destinationUserId, destinationAppId);

            Assert.That(await service.ReadSourceFileAsync(
                destinationUserId, destinationAppId, "backend/linked-sentinel.txt"), Is.Null);
            Assert.That(await service.ReadSourceFileAsync(
                destinationUserId, destinationAppId, "backend/linked-private/sentinel.txt"), Is.Null);
            Assert.That(await File.ReadAllTextAsync(sentinel), Is.EqualTo("private host data"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
