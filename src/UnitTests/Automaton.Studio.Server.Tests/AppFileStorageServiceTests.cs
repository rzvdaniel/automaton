using Automaton.Studio.Server.Services;
using System.Reflection;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class AppFileStorageServiceTests
{
    private static readonly MethodInfo IsBuildBasedFrontendSourcePathMethod =
        typeof(AppFileStorageService).GetMethod("IsBuildBasedFrontendSourcePath", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(AppFileStorageService), "IsBuildBasedFrontendSourcePath");

    [Test]
    public void IsBuildBasedFrontendSourcePath_ReturnsTrue_ForViteStyleSourceTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "package.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "public"));

            var result = InvokeIsBuildBasedFrontendSourcePath(root);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void IsBuildBasedFrontendSourcePath_ReturnsFalse_ForPlainStaticFrontend()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>Static</title>");
            File.WriteAllText(Path.Combine(root, "styles.css"), "body { margin: 0; }");
            File.WriteAllText(Path.Combine(root, "app.js"), "console.log('static');");

            var result = InvokeIsBuildBasedFrontendSourcePath(root);

            Assert.That(result, Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool InvokeIsBuildBasedFrontendSourcePath(string path)
    {
        try
        {
            return (bool)IsBuildBasedFrontendSourcePathMethod.Invoke(null, [path])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
