using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public class PublishedAppStorageBudgetTests
{
    [Test]
    public async Task ValidateWrite_RejectsCombinedPublishedRootBytes()
    {
        var root = CreateRoot();
        try
        {
            var liveApp = Path.Combine(root, "live", "index.html");
            var stagedApp = Path.Combine(root, ".staging", "upload", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(liveApp)!);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedApp)!);
            await File.WriteAllBytesAsync(liveApp, [1, 2, 3]);
            var budget = new PublishedAppStorageBudget(maximumBytes: 5, maximumFiles: 10);

            Assert.Throws<InvalidDataException>(() => budget.ValidateWrite(root, stagedApp, replacementBytes: 3));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ValidateWrite_CountsFileReplacementOnce()
    {
        var root = CreateRoot();
        try
        {
            var filePath = Path.Combine(root, "slug", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4]);
            var budget = new PublishedAppStorageBudget(maximumBytes: 4, maximumFiles: 1);

            Assert.DoesNotThrow(() => budget.ValidateWrite(root, filePath, replacementBytes: 4));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ValidateRoot_RejectsExistingFilesOverAggregateLimit()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "slug"));
            await File.WriteAllBytesAsync(Path.Combine(root, "slug", "index.html"), [1, 2, 3]);
            var budget = new PublishedAppStorageBudget(maximumBytes: 2, maximumFiles: 10);

            Assert.Throws<InvalidDataException>(() => budget.ValidateRoot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ValidateWrite_RejectsCombinedPublishedRootFileCount()
    {
        var root = CreateRoot();
        try
        {
            var existingFile = Path.Combine(root, "live", "index.html");
            var newFile = Path.Combine(root, ".staging", "upload", "index.html");
            Directory.CreateDirectory(Path.GetDirectoryName(existingFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(newFile)!);
            await File.WriteAllBytesAsync(existingFile, [1]);
            var budget = new PublishedAppStorageBudget(maximumBytes: 10, maximumFiles: 1);

            Assert.Throws<InvalidDataException>(() => budget.ValidateWrite(root, newFile, replacementBytes: 1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-published-budget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
