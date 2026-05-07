using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class LocalAppValidationServiceTests
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
    public async Task ValidateAsync_WhenFrontendOnlyApp_ReturnsSuccessfulChecks()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Test uses a Unix-style fake python executable.");
        }

        var projectRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(projectRoot, "index.html"), "<html></html>");
        var binDir = Path.Combine(projectRoot, ".venv", "bin");
        Directory.CreateDirectory(binDir);
        var fakePython = Path.Combine(binDir, "python");
        await File.WriteAllTextAsync(fakePython, "#!/bin/sh\necho Python 9.9.9\n");
        File.SetUnixFileMode(fakePython, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var configService = new ConfigService();
        var projectService = new LocalAppProjectService(configService);
        var pythonRuntimeService = new PythonRuntimeService(configService);
        var executionService = new LocalAppExecutionService(pythonRuntimeService);
        var validationService = new LocalAppValidationService(projectService, executionService, pythonRuntimeService);

        var result = await validationService.ValidateAsync(projectRoot);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Checks.Any(check => check.Name == "indexHtml" && check.Status == "pass"), Is.True);
        Assert.That(result.Checks.Any(check => check.Name == "python" && check.Status == "pass"), Is.True);
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
