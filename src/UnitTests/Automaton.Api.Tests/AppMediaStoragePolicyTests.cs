using Automaton.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppMediaStoragePolicyTests
{
    [Test]
    public void TryValidateUpload_RejectsUnsupportedExtension()
    {
        var file = CreateFormFile("script.js", "application/javascript", 100);

        var valid = AppMediaStoragePolicy.TryValidateUpload(file, 1024, out var error, out _, out _);

        Assert.That(valid, Is.False);
        Assert.That(error, Is.EqualTo("Unsupported file type."));
    }

    [Test]
    public void TryValidateUpload_RejectsTooLargeFiles()
    {
        var file = CreateFormFile("image.png", "image/png", 2048);

        var valid = AppMediaStoragePolicy.TryValidateUpload(file, 1024, out var error, out _, out _);

        Assert.That(valid, Is.False);
        Assert.That(error, Does.Contain("upload limit"));
    }

    [Test]
    public void BuildStorageRelativePath_ReturnsUploadPathUnderMediaContract()
    {
        var path = AppMediaStoragePolicy.BuildStorageRelativePath("My first post.png", new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(path, Does.StartWith("uploads/2026/06/my-first-post-"));
        Assert.That(path, Does.EndWith(".png"));
    }

    private static IFormFile CreateFormFile(string fileName, string contentType, int length)
    {
        var bytes = new byte[length];
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
