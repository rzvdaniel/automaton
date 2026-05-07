using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class PythonRuntimeServiceTests
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
    public async Task ResolveAsync_WhenLocalVenvExists_PrefersIt()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Test uses a Unix-style fake python executable.");
        }

        var projectRoot = CreateTempDirectory();
        var binDir = Path.Combine(projectRoot, ".venv", "bin");
        Directory.CreateDirectory(binDir);

        var fakePython = Path.Combine(binDir, "python");
        await File.WriteAllTextAsync(fakePython, "#!/bin/sh\necho Python 9.9.9\n");
        File.SetUnixFileMode(fakePython, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var service = new PythonRuntimeService(new ConfigService());

        var runtime = await service.ResolveAsync(projectRoot);

        Assert.That(runtime.ExecutablePath, Is.EqualTo(fakePython));
        Assert.That(runtime.Version, Does.Contain("Python 9.9.9"));
        Assert.That(runtime.Source, Is.EqualTo("app-local-venv"));
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
