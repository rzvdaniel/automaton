using System.IO.Compression;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class ZipServiceTests
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
    public void SaveProjectConfig_WhenIgnoreFileMissing_CreatesAutomatonIgnoreWithDefaults()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        var configService = new ConfigService();

        // Act
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "Test App",
            Server = "https://api.automaton.monster"
        });

        // Assert
        var ignoreFile = Path.Combine(projectRoot, ".automatonignore");
        Assert.That(File.Exists(ignoreFile), Is.True);

        var content = File.ReadAllText(ignoreFile);
        Assert.That(content, Does.Contain(".automaton/"));
        Assert.That(content, Does.Contain("node_modules/"));
        Assert.That(content, Does.Contain("automaton.json"));
        Assert.That(content, Does.Contain(".automatonignore"));
    }

    [Test]
    public void SaveProjectConfig_WhenIgnoreFileExists_DoesNotOverwriteUserContent()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        var ignoreFile = Path.Combine(projectRoot, ".automatonignore");
        File.WriteAllText(ignoreFile, "custom-pattern.txt");

        var configService = new ConfigService();

        // Act
        configService.SaveProjectConfig(projectRoot, new AutomatonConfig
        {
            AppId = Guid.NewGuid().ToString(),
            Name = "Test App"
        });

        // Assert
        Assert.That(File.ReadAllText(ignoreFile), Is.EqualTo("custom-pattern.txt"));
    }

    [Test]
    public void CreateZip_WhenPackagingRoot_ExcludesBuiltInIgnoredPaths()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(projectRoot, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(projectRoot, "AGENTS.md"), "agent guidance");
        File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "claude guidance");
        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".automaton"));
        File.WriteAllText(Path.Combine(projectRoot, ".automaton", "config.json"), "{}");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".github", "skills", "automaton-app"));
        File.WriteAllText(Path.Combine(projectRoot, ".github", "copilot-instructions.md"), "copilot");
        File.WriteAllText(Path.Combine(projectRoot, ".github", "skills", "automaton-app", "SKILL.md"), "skill");

        Directory.CreateDirectory(Path.Combine(projectRoot, "node_modules", "pkg"));
        File.WriteAllText(Path.Combine(projectRoot, "node_modules", "pkg", "index.js"), "console.log('ignored');");

        // Act
        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        // Assert
        Assert.That(entries, Does.Contain("index.html"));
        Assert.That(entries, Does.Not.Contain(".automaton/config.json"));
        Assert.That(entries, Does.Not.Contain(".github/copilot-instructions.md"));
        Assert.That(entries, Does.Not.Contain(".github/skills/automaton-app/SKILL.md"));
        Assert.That(entries, Does.Not.Contain("AGENTS.md"));
        Assert.That(entries, Does.Not.Contain("CLAUDE.md"));
        Assert.That(entries, Does.Not.Contain("automaton.json"));
        Assert.That(entries, Does.Not.Contain("node_modules/pkg/index.js"));
    }

    [Test]
    public void CreateSourceZip_WhenPackagingProjectRoot_IncludesAutomatonConfigButExcludesBuildArtifacts()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "src"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "dist", "assets"));

        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "src", "main.js"), "console.log('source');");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "dist", "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "dist", "assets", "app.js"), "console.log('runtime');");

        using var zipStream = ZipService.CreateSourceZip(projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("automaton.json"));
        Assert.That(entries, Does.Contain("frontend/src/main.js"));
        Assert.That(entries, Does.Not.Contain("frontend/dist/index.html"));
        Assert.That(entries, Does.Not.Contain("frontend/dist/assets/app.js"));
    }

    [Test]
    public void CreateZip_WhenAutomatonIgnoreContainsCustomPatterns_ExcludesMatchingFilesFromDeployDir()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        var deployDir = Path.Combine(projectRoot, "dist");
        Directory.CreateDirectory(deployDir);

        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"), "*.map" + Environment.NewLine + "coverage/");
        File.WriteAllText(Path.Combine(deployDir, "app.js"), "console.log('keep');");
        File.WriteAllText(Path.Combine(deployDir, "app.js.map"), "ignored-map");
        Directory.CreateDirectory(Path.Combine(deployDir, "coverage"));
        File.WriteAllText(Path.Combine(deployDir, "coverage", "report.txt"), "ignored-coverage");

        // Act
        using var zipStream = ZipService.CreateZip(projectRoot, deployDir);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        // Assert
        Assert.That(entries, Does.Contain("app.js"));
        Assert.That(entries, Does.Not.Contain("app.js.map"));
        Assert.That(entries, Does.Not.Contain("coverage/report.txt"));
    }

    [Test]
    public void CreateZip_WhenAutomatonIgnoreContainsNegation_ThrowsClearError()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"), "!keep-me.txt");
        File.WriteAllText(Path.Combine(projectRoot, "keep-me.txt"), "content");

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => ZipService.CreateZip(projectRoot, projectRoot));

        // Assert
        Assert.That(ex!.Message, Does.Contain("does not support negation patterns yet"));
    }

    [Test]
    public void ResolveDeployDirectory_WhenDistExists_PrefersDist()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "dist"));

        var resolved = ZipService.ResolveDeployDirectory(projectRoot, "frontend/dist");

        Assert.That(resolved, Is.EqualTo(Path.Combine(projectRoot, "frontend", "dist")));
    }

    [Test]
    public void ResolveDeployDirectory_WhenDeployDirIsMissing_ThrowsClearError()
    {
        var projectRoot = CreateTempDirectory();

        var ex = Assert.Throws<InvalidOperationException>(() => ZipService.ResolveDeployDirectory(projectRoot, null));

        Assert.That(ex!.Message, Does.Contain("frontend.deployDir is required"));
    }

    [Test]
    public void ResolveDeployDirectory_WhenDeployDirPointsOutsideProject_ThrowsClearError()
    {
        var projectRoot = CreateTempDirectory();

        var ex = Assert.Throws<InvalidOperationException>(() => ZipService.ResolveDeployDirectory(projectRoot, ".."));

        Assert.That(ex!.Message, Does.Contain("points outside the project root"));
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "automaton-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }
}
