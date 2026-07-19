using Automaton.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Automaton.Api.Tests;

[TestFixture]
public class AdminDocumentationServiceTests
{
    [Test]
    public void GetAsset_ReturnsAllowedAssetWithinDocumentationGroup()
    {
        var root = CreateRoot();

        try
        {
            var assetPath = Path.Combine(root, "Documentation", "Admin", "hosted-runners", "assets", "diagram.png");
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.WriteAllBytes(assetPath, [1, 2, 3]);

            var asset = CreateService(root).GetAsset("hosted-runners", "diagram.png");

            Assert.That(asset, Is.Not.Null);
            Assert.That(asset!.AbsolutePath, Is.EqualTo(assetPath));
            Assert.That(asset.ContentType, Is.EqualTo("image/png"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("../diagram.png")]
    [TestCase("nested/../../diagram.png")]
    [TestCase("diagram.exe")]
    [TestCase("missing.png")]
    public void GetAsset_ReturnsNullForUnsafeOrUnavailableAsset(string relativePath)
    {
        var root = CreateRoot();

        try
        {
            var assetsPath = Path.Combine(root, "Documentation", "Admin", "hosted-runners", "assets");
            Directory.CreateDirectory(assetsPath);
            File.WriteAllBytes(Path.Combine(assetsPath, "diagram.png"), [1, 2, 3]);

            var asset = CreateService(root).GetAsset("hosted-runners", relativePath);

            Assert.That(asset, Is.Null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AdminDocumentationService CreateService(string contentRootPath) =>
        new(new StubWebHostEnvironment(contentRootPath));

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-documentation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class StubWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
