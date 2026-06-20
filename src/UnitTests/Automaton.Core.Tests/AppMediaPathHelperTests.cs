using Automaton.Core.Models;

namespace Automaton.Core.Tests;

[TestFixture]
public class AppMediaPathHelperTests
{
    [TestCase("../secret.txt")]
    [TestCase("/uploads/file.jpg")]
    [TestCase("uploads/../../secret.txt")]
    [TestCase("C:\\Users\\me\\secret.txt")]
    [TestCase("uploads\\..\\..\\secret.txt")]
    public void NormalizeRelativePath_RejectsUnsafePaths(string path)
    {
        Assert.That(
            () => AppMediaPathHelper.NormalizeRelativePath(path),
            Throws.InstanceOf<Exception>());
    }

    [Test]
    public void NormalizeRelativePath_NormalizesBackslashes()
    {
        var result = AppMediaPathHelper.NormalizeRelativePath("uploads\\2026\\06\\file.jpg");

        Assert.That(result, Is.EqualTo("uploads/2026/06/file.jpg"));
    }

    [Test]
    public void ToPublicPath_PrefixesMediaSegment()
    {
        var result = AppMediaPathHelper.ToPublicPath("uploads/2026/06/file.jpg");

        Assert.That(result, Is.EqualTo("/media/uploads/2026/06/file.jpg"));
    }
}
