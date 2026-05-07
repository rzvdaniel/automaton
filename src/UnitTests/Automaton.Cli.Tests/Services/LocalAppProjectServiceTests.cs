using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class LocalAppProjectServiceTests
{
    private readonly List<string> _tempDirectories = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _tempDirectories)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        _tempDirectories.Clear();
    }

    [Test]
    public void ResolveContext_WhenCalledFromNestedDirectory_FindsProjectRoot()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "nested"));
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "index.html"), "<html></html>");

        var service = new LocalAppProjectService(new ConfigService());

        var context = service.ResolveContext(Path.Combine(projectRoot, "frontend", "nested"));

        Assert.That(context.RootPath, Is.EqualTo(projectRoot));
        Assert.That(context.FrontendPath, Is.EqualTo(Path.Combine(projectRoot, "frontend")));
        Assert.That(context.IndexHtmlPath, Is.EqualTo(Path.Combine(projectRoot, "frontend", "index.html")));
    }

    [Test]
    public void ResolveContext_WhenAutomatonJsonSpecifiesDeployDir_UsesThatFrontend()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "dist"));
        File.WriteAllText(Path.Combine(projectRoot, "dist", "index.html"), "<html></html>");

        var configService = new ConfigService();
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "Sample App",
            DeployDir = "dist"
        });

        var service = new LocalAppProjectService(configService);

        var context = service.ResolveContext(projectRoot);

        Assert.That(context.FrontendPath, Is.EqualTo(Path.Combine(projectRoot, "dist")));
        Assert.That(context.DisplayName, Is.EqualTo("Sample App"));
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
