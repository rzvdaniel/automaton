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
        Assert.That(content, Does.Contain("node_modules/"));
        Assert.That(content, Does.Contain("!.env.example"));
        Assert.That(content, Does.Not.Contain("automaton.json"));
        Assert.That(content, Does.Not.Contain(".automatonignore"));
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
        Assert.That(entries, Does.Contain(".github/copilot-instructions.md"));
        Assert.That(entries, Does.Contain(".github/skills/automaton-app/SKILL.md"));
        Assert.That(entries, Does.Contain("AGENTS.md"));
        Assert.That(entries, Does.Contain("CLAUDE.md"));
        Assert.That(entries, Does.Not.Contain("automaton.json"));
        Assert.That(entries, Does.Contain("node_modules/pkg/index.js"));
    }

    [Test]
    public void CreateSourceZip_WhenPackagingProjectRoot_IncludesAutomatonConfigButExcludesBuildArtifacts()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "src"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "dist", "assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend", "data"));

        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, ".gitignore"), "dist/");
        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"),
            ".gitignore" + Environment.NewLine
            + ".env.*" + Environment.NewLine
            + "*.log" + Environment.NewLine
            + "*.sqlite" + Environment.NewLine
            + "dist/");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "src", "main.js"), "console.log('source');");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", ".env.production"), "SECRET=value");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "debug.log"), "local log");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "data", "local.sqlite"), "local database");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "dist", "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "dist", "assets", "app.js"), "console.log('runtime');");

        using var zipStream = ZipService.CreateSourceZip(projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("automaton.json"));
        Assert.That(entries, Does.Contain("frontend/src/main.js"));
        Assert.That(entries, Does.Not.Contain(".gitignore"));
        Assert.That(entries, Does.Not.Contain("frontend/.env.production"));
        Assert.That(entries, Does.Not.Contain("backend/debug.log"));
        Assert.That(entries, Does.Not.Contain("backend/data/local.sqlite"));
        Assert.That(entries, Does.Not.Contain("frontend/dist/index.html"));
        Assert.That(entries, Does.Not.Contain("frontend/dist/assets/app.js"));
    }

    [Test]
    public void CreateSourceZip_WhenProjectContainsNestedNodeModules_ExcludesDependencyTree()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "src"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "node_modules", "vite"));

        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"), "node_modules/");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "src", "main.js"), "console.log('source');");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "node_modules", "vite", "index.js"), "console.log('dep');");

        using var zipStream = ZipService.CreateSourceZip(projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("automaton.json"));
        Assert.That(entries, Does.Contain("frontend/src/main.js"));
        Assert.That(entries, Does.Not.Contain("frontend/node_modules/vite/index.js"));
    }

    [Test]
    public void CreateSourceZip_WhenProjectContainsNestedBuildAndVirtualEnvArtifacts_ExcludesThem()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "src"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "dist"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend", ".venv", "lib"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend", "__pycache__"));

        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"),
            "dist/" + Environment.NewLine + ".venv/" + Environment.NewLine + "__pycache__/");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "src", "main.ts"), "console.log('source');");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "dist", "index.js"), "console.log('build');");
        File.WriteAllText(Path.Combine(projectRoot, "backend", ".venv", "lib", "x.py"), "print('venv')");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "__pycache__", "main.cpython-312.pyc"), "compiled");

        using var zipStream = ZipService.CreateSourceZip(projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("automaton.json"));
        Assert.That(entries, Does.Contain("frontend/src/main.ts"));
        Assert.That(entries, Does.Not.Contain("frontend/dist/index.js"));
        Assert.That(entries, Does.Not.Contain("backend/.venv/lib/x.py"));
        Assert.That(entries, Does.Not.Contain("backend/__pycache__/main.cpython-312.pyc"));
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
    public void CreateZip_WhenAutomatonIgnoreContainsNegation_ReincludesMatchingFile()
    {
        // Arrange
        var projectRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"), "*.txt" + Environment.NewLine + "!keep-me.txt");
        File.WriteAllText(Path.Combine(projectRoot, "keep-me.txt"), "content");
        File.WriteAllText(Path.Combine(projectRoot, "skip-me.txt"), "content");

        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("keep-me.txt"));
        Assert.That(entries, Does.Not.Contain("skip-me.txt"));
    }

    [Test]
    public void CreateZip_WhenPatternIsRooted_OnlyExcludesRootMatch()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "dist"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "nested", "dist"));
        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"), "/dist/\r\n/.env\r\n");
        File.WriteAllText(Path.Combine(projectRoot, "dist", "root.js"), "skip");
        File.WriteAllText(Path.Combine(projectRoot, "nested", "dist", "nested.js"), "keep");
        File.WriteAllText(Path.Combine(projectRoot, ".env"), "skip");
        File.WriteAllText(Path.Combine(projectRoot, "nested", ".env"), "keep");

        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Not.Contain("dist/root.js"));
        Assert.That(entries, Does.Contain("nested/dist/nested.js"));
        Assert.That(entries, Does.Not.Contain(".env"));
        Assert.That(entries, Does.Contain("nested/.env"));
    }

    [Test]
    public void CreateZip_WhenFilenameGlobIsUnrooted_ExcludesMatchesAtAnyDepth()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend", "data"));
        File.WriteAllText(Path.Combine(projectRoot, ".automatonignore"), "*.db");
        File.WriteAllText(Path.Combine(projectRoot, "root.db"), "skip");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "data", "nested.db"), "skip");

        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Not.Contain("root.db"));
        Assert.That(entries, Does.Not.Contain("backend/data/nested.db"));
    }

    [Test]
    public void CreateZip_WhenLaterRuleMatches_UsesLastMatchingRule()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend", "fixtures"));
        File.WriteAllText(
            Path.Combine(projectRoot, ".automatonignore"),
            "*.db\r\n!backend\\fixtures\\example.db\r\n*.db\r\n");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "fixtures", "example.db"), "skip");

        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Not.Contain("backend/fixtures/example.db"));
    }

    [Test]
    public void CreateZip_WhenNestedDatabaseIsNegated_ReincludesIt()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend", "fixtures"));
        File.WriteAllText(
            Path.Combine(projectRoot, ".automatonignore"),
            "*.db\r\n!backend/fixtures/example.db\r\n");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "fixtures", "example.db"), "keep");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "fixtures", "local.db"), "skip");

        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("backend/fixtures/example.db"));
        Assert.That(entries, Does.Not.Contain("backend/fixtures/local.db"));
    }

    [Test]
    public void CreateZip_WhenIgnoredDirectoryHasPathSpecificNegations_ReopensSubtree()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "fixtures", "private"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "fixtures", "public"));
        File.WriteAllText(
            Path.Combine(projectRoot, ".automatonignore"),
            "fixtures/\r\n!fixtures/public/\r\n!fixtures/public/example.db\r\n");
        File.WriteAllText(Path.Combine(projectRoot, "fixtures", "private", "secret.db"), "skip");
        File.WriteAllText(Path.Combine(projectRoot, "fixtures", "public", "example.db"), "keep");

        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Not.Contain("fixtures/private/secret.db"));
        Assert.That(entries, Does.Contain("fixtures/public/example.db"));
    }

    [Test]
    public void CreateZip_WhenIgnoreFileHasWhitespaceCommentsAndCrLf_ParsesMeaningfulRules()
    {
        var projectRoot = CreateTempDirectory();
        File.WriteAllText(
            Path.Combine(projectRoot, ".automatonignore"),
            "\r\n  # comment\r\n  *.log  \r\n\r\n");
        File.WriteAllText(Path.Combine(projectRoot, "debug.log"), "skip");
        File.WriteAllText(Path.Combine(projectRoot, "index.html"), "keep");

        using var zipStream = ZipService.CreateZip(projectRoot, projectRoot);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Not.Contain("debug.log"));
        Assert.That(entries, Does.Contain("index.html"));
    }

    [Test]
    public void CreateZip_WhenScopeExcludeMatches_SkipsFile()
    {
        var projectRoot = CreateTempDirectory();
        var deployDir = Path.Combine(projectRoot, "backend");
        Directory.CreateDirectory(Path.Combine(deployDir, "cache"));

        File.WriteAllText(Path.Combine(deployDir, "todos.py"), "print('keep')");
        File.WriteAllText(Path.Combine(deployDir, "cache", "data.json"), "{}");

        using var zipStream = ZipService.CreateZip(projectRoot, deployDir, new DeployScopeConfig
        {
            Exclude = ["cache/**"]
        });
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("todos.py"));
        Assert.That(entries, Does.Not.Contain("cache/data.json"));
    }

    [Test]
    public void CreateZip_WhenScopeIncludeSpecified_OnlyIncludesMatchingFiles()
    {
        var projectRoot = CreateTempDirectory();
        var deployDir = Path.Combine(projectRoot, "backend");
        Directory.CreateDirectory(Path.Combine(deployDir, "data"));

        File.WriteAllText(Path.Combine(deployDir, "todos.py"), "print('keep')");
        File.WriteAllText(Path.Combine(deployDir, "README.md"), "skip");
        File.WriteAllText(Path.Combine(deployDir, "data", "todos.json"), "{}");

        using var zipStream = ZipService.CreateZip(projectRoot, deployDir, new DeployScopeConfig
        {
            Include = ["*.py", "data/**"]
        });
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("todos.py"));
        Assert.That(entries, Does.Contain("data/todos.json"));
        Assert.That(entries, Does.Not.Contain("README.md"));
    }

    [Test]
    public void CreateSourceZip_WhenSourceScopeExcludeMatches_SkipsFile()
    {
        var projectRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(projectRoot, "frontend", "src"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "backend"));

        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, "frontend", "src", "main.js"), "console.log('keep');");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "secret.tmp"), "skip");

        using var zipStream = ZipService.CreateSourceZip(projectRoot, new DeployScopeConfig
        {
            Exclude = ["backend/*.tmp"]
        });
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(e => e.FullName).ToArray();

        Assert.That(entries, Does.Contain("automaton.json"));
        Assert.That(entries, Does.Contain("frontend/src/main.js"));
        Assert.That(entries, Does.Not.Contain("backend/secret.tmp"));
    }

    [Test]
    public void CreateDeployPackageZip_WhenBackendRuntimeDiffersFromSource_SeparatesArchiveSurfaces()
    {
        var projectRoot = CreateTempDirectory();
        var frontendDeployDirectory = Path.Combine(projectRoot, "frontend", "dist");
        var backendDeployDirectory = Path.Combine(projectRoot, "backend", "dist");
        Directory.CreateDirectory(frontendDeployDirectory);
        Directory.CreateDirectory(backendDeployDirectory);

        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, "backend", "controller.py"), "print('source')");
        File.WriteAllText(Path.Combine(backendDeployDirectory, "controller.py"), "print('runtime')");
        File.WriteAllText(Path.Combine(frontendDeployDirectory, "index.html"), "<html></html>");

        using var zipStream = ZipService.CreateDeployPackageZip(
            projectRoot,
            "app-123",
            frontendDeployDirectory,
            backendDeployDirectory,
            sourceScopeConfig: null,
            frontendConfig: new FrontendConfig(),
            backendConfig: new BackendConfig
            {
                SourceDir = "backend",
                DeployDir = "backend/dist"
            });
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.That(entries, Does.Contain("source/backend/controller.py"));
        Assert.That(entries, Does.Not.Contain("source/backend/dist/controller.py"));
        Assert.That(entries, Does.Contain("runtime/backend/controller.py"));
        Assert.That(entries, Does.Contain("runtime/frontend/index.html"));
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
