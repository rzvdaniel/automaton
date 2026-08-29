using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class RunnerWorkspaceServiceTests
{
    private RunnerWorkspaceService service = null!;
    private string? originalWorkspaceRoot;
    private string? originalScriptsDir;

    [SetUp]
    public void SetUp()
    {
        originalWorkspaceRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalScriptsDir = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", null);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", null);
        service = new RunnerWorkspaceService();
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalWorkspaceRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", originalScriptsDir);
    }

    [Test]
    public void UsesWorkspaceRootLayout_WhenWorkspaceRootIsConfigured()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);

        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);

        Assert.That(service.WorkspaceRoot, Is.EqualTo(fullWorkspaceRoot));
        Assert.That(service.GetLegacyControllerBackendDirectory(appId, targetId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps", appId.ToString(), "targets", targetId.ToString(), "runtime", "backend")));
        Assert.That(service.GetAppDataDirectory(appId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "data", appId.ToString())));
        Assert.That(service.GetLegacyControllerVenvDirectory(appId, targetId), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps", appId.ToString(), "targets", targetId.ToString(), "venv")));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.Combine(fullWorkspaceRoot, "apps")));
    }

    [Test]
    public void UsesRunnerScriptsDirAsFastApiCompatibilityBridge_WhenProvided()
    {
        var scriptsRoot = Path.Combine(Path.GetTempPath(), "runner-apps", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", scriptsRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        Assert.That(service.UsesLegacyScriptsLayout, Is.True);
        Assert.That(service.AppsRoot, Is.EqualTo(Path.GetFullPath(Path.Combine(scriptsRoot, "..", "apps"))));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.GetFullPath(scriptsRoot)));
        Assert.That(service.GetLegacyControllerBackendDirectory(appId, targetId), Is.EqualTo(Path.Combine(
            Path.GetFullPath(scriptsRoot), appId.ToString(), "targets", targetId.ToString(), "runtime", "backend")));
        Assert.That(service.GetLegacyControllerVenvDirectory(appId, targetId), Is.EqualTo(Path.Combine(
            Path.GetFullPath(scriptsRoot), appId.ToString(), "targets", targetId.ToString(), "venv")));
        Assert.That(service.GetLegacyControllerDataDirectory(appId), Is.EqualTo(Path.Combine(
            Path.GetFullPath(scriptsRoot), appId.ToString(), "data")));
        Assert.That(service.GetAppDataDirectory(appId), Is.EqualTo(Path.GetFullPath(Path.Combine(scriptsRoot, "..", "data", appId.ToString()))));
    }

    [Test]
    public void ResolvesRunnerDataFilesUnderWorkspaceDataRoot()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var resolved = service.ResolveRunnerFilePath(appId, targetId, "nested/file.txt", AppRunnerFileAreas.Data);

        Assert.That(resolved, Is.EqualTo(Path.Combine(Path.GetFullPath(workspaceRoot), "data", appId.ToString(), "nested", "file.txt")));
    }

    [Test]
    public void ResolvesActionRuntimePathsUnderAppRuntimeRoot()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var current = service.GetAppActionCurrentDirectory(appId, targetId, "send-email");

        Assert.That(current, Is.EqualTo(Path.Combine(Path.GetFullPath(workspaceRoot), "apps", appId.ToString(), "targets", targetId.ToString(), "artifacts", "current", "actions", "send-email", "current")));
        Assert.That(() => service.GetAppActionCurrentDirectory(appId, targetId, "../escape"), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void RejectsTraversalOutsideWorkspaceRoots()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        Assert.That(() => service.ResolveBackendUploadPath(appId, targetId, "../escape.py"), Throws.TypeOf<ArgumentException>());
        Assert.That(() => service.ResolveRunnerFilePath(appId, targetId, "..\\escape.txt", AppRunnerFileAreas.Data), Throws.TypeOf<ArgumentException>());
        Assert.That(() => service.ResolveAppMediaFilePath(appId, "/etc/passwd"), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void UsesLocalWorkspaceFallback_WhenWorkspaceRootIsNotConfigured()
    {
        var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "workspace"));

        Assert.That(service.WorkspaceRoot, Is.EqualTo(expected));
        Assert.That(service.GetFastApiScriptsRoot(), Is.EqualTo(Path.Combine(expected, "apps")));
    }

    [Test]
    public void RuntimePaths_AreIsolatedByDeploymentTarget()
    {
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "runner-workspace",
            Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var firstTargetId = Guid.NewGuid();
        var secondTargetId = Guid.NewGuid();

        var firstBackend = service.GetLegacyControllerBackendDirectory(appId, firstTargetId);
        var secondBackend = service.GetLegacyControllerBackendDirectory(appId, secondTargetId);
        var firstLog = Path.Combine(service.GetAppLogsRoot(appId, firstTargetId), "backend.log");
        var secondLog = Path.Combine(service.GetAppLogsRoot(appId, secondTargetId), "backend.log");

        Assert.Multiple(() =>
        {
            Assert.That(firstBackend, Is.Not.EqualTo(secondBackend));
            Assert.That(firstLog, Is.Not.EqualTo(secondLog));
            Assert.That(firstBackend, Does.Contain(
                Path.Combine("targets", firstTargetId.ToString())));
            Assert.That(secondBackend, Does.Contain(
                Path.Combine("targets", secondTargetId.ToString())));
        });
    }

    [Test]
    public void LegacyControllerBridge_UsesThePathsConsumedByTheControllerRuntime()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var backendRoot = service.GetLegacyControllerBackendDirectory(appId, targetId);
        var uploadedFile = service.ResolveBackendUploadPath(appId, targetId, "controllers/todos.py");

        Assert.Multiple(() =>
        {
            Assert.That(backendRoot, Is.EqualTo(Path.Combine(
                service.GetDeploymentTargetRoot(appId, targetId), "runtime", "backend")));
            Assert.That(uploadedFile, Is.EqualTo(Path.Combine(backendRoot, "controllers", "todos.py")));
            Assert.That(service.GetLegacyControllerVenvDirectory(appId, targetId), Is.EqualTo(
                Path.Combine(service.GetDeploymentTargetRoot(appId, targetId), "venv")));
            Assert.That(service.GetLegacyControllerDataDirectory(appId), Is.EqualTo(
                service.GetAppDataDirectory(appId)));
        });
    }

    [Test]
    public void RuntimeRegistry_AllowsTwoTargetsOfOneApp()
    {
        var registry = new RunnerRuntimeRegistry();
        var appId = Guid.NewGuid();
        var firstTargetId = Guid.NewGuid();
        var secondTargetId = Guid.NewGuid();

        registry.Register(appId, firstTargetId, 18001);
        registry.Register(appId, secondTargetId, 18002);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, firstTargetId, out var first), Is.True);
            Assert.That(first.Port, Is.EqualTo(18001));
            Assert.That(registry.TryGet(appId, secondTargetId, out var second), Is.True);
            Assert.That(second.Port, Is.EqualTo(18002));
        });

        registry.Remove(appId, firstTargetId);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGet(appId, firstTargetId, out _), Is.False);
            Assert.That(registry.TryGet(appId, secondTargetId, out _), Is.True);
        });
    }

    [Test]
    public void RuntimePaths_RejectMissingTargetIdentity()
    {
        Assert.That(
            () => service.GetLegacyControllerBackendDirectory(Guid.NewGuid(), Guid.Empty),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ApplicationStorageRoots_SeparateDisposableAndPersistentState()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var paths = service.GetApplicationStoragePaths(Guid.NewGuid(), Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(paths.SourceRoot, Does.Contain(Path.Combine("workspace", "source")));
            Assert.That(paths.ArtifactsRoot, Does.Contain("artifacts"));
            Assert.That(paths.RuntimeRoot, Does.Contain("runtime"));
            Assert.That(paths.LogsRoot, Does.Contain("logs"));
            Assert.That(paths.CacheRoot, Does.Contain("cache"));
            Assert.That(paths.PersistentDataRoot, Does.Contain("data"));
            Assert.That(paths.PersistentDataRoot, Is.Not.EqualTo(paths.ArtifactsRoot));
        });
    }

    [Test]
    public void PrepareApplicationStorage_ResolvesMountsInsidePersistentData()
    {
        var appId = Guid.NewGuid();
        var storage = new ApplicationStorageSpec
        {
            DataDirectory = "app-data",
            Mounts = [new ApplicationStorageMount { Name = "uploads", RelativePath = "media/uploads" }]
        };

        var prepared = service.PrepareApplicationStorage(appId, storage);

        Assert.Multiple(() =>
        {
            Assert.That(prepared.DataDirectory, Does.EndWith(Path.Combine("data", appId.ToString(), "app-data")));
            Assert.That(prepared.Mounts["uploads"], Does.EndWith(Path.Combine("data", appId.ToString(), "media", "uploads")));
            Assert.That(Directory.Exists(prepared.DataDirectory), Is.True);
            Assert.That(Directory.Exists(prepared.Mounts["uploads"]), Is.True);
        });
    }

    [Test]
    public void ResolveApplicationDataPath_RejectsTraversalAndAbsolutePaths()
    {
        var appId = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(() => service.ResolveApplicationDataPath(appId, "../outside"), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => service.ResolveApplicationDataPath(appId, "/outside"), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => service.ResolveApplicationDataPath(appId, "C:\\outside"), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void DeleteApplicationTarget_PreservesPersistentApplicationData()
    {
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var source = service.GetAppSourceRoot(appId, targetId);
        var data = service.GetAppDataDirectory(appId);
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(source, "source.txt"), "disposable");
        File.WriteAllText(Path.Combine(data, "user.db"), "durable");

        service.DeleteApplicationTarget(appId, targetId);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(service.GetDeploymentTargetRoot(appId, targetId)), Is.False);
            Assert.That(File.Exists(Path.Combine(data, "user.db")), Is.True);
        });
    }

    [Test]
    public void MigrateLegacyLayout_MovesSourceArtifactsMetadataAndDataOnce()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var oldTargetRoot = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            "apps",
            appId.ToString(),
            "targets",
            targetId.ToString());
        var oldArtifactRoot = Path.Combine(oldTargetRoot, "runtime", "application");
        var legacyControllerBackend = Path.Combine(oldTargetRoot, "runtime", "backend");
        var legacyControllerVenv = Path.Combine(oldTargetRoot, "venv");
        Directory.CreateDirectory(Path.Combine(oldTargetRoot, "source"));
        Directory.CreateDirectory(oldArtifactRoot);
        Directory.CreateDirectory(legacyControllerBackend);
        Directory.CreateDirectory(legacyControllerVenv);
        var oldData = Path.Combine(Path.GetFullPath(workspaceRoot), "apps", appId.ToString(), "data");
        Directory.CreateDirectory(oldData);
        File.WriteAllText(Path.Combine(oldTargetRoot, "source", "source.txt"), "source");
        File.WriteAllText(Path.Combine(oldArtifactRoot, "runtime.txt"), "artifact");
        File.WriteAllText(Path.Combine(legacyControllerBackend, "todos.py"), "controller");
        File.WriteAllText(Path.Combine(legacyControllerVenv, "marker.txt"), "venv");
        File.WriteAllText(Path.Combine(oldData, "user.db"), "durable");
        File.WriteAllText(
            Path.Combine(oldTargetRoot, "deployment.json"),
            JsonSerializer.Serialize(new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = targetId.ToString(),
                Runtime = new ApplicationRuntimeSpec(),
                ArtifactRoot = oldArtifactRoot,
                State = RuntimeReadinessState.Ready
            }));

        service.MigrateLegacyLayout();
        service.MigrateLegacyLayout();

        var metadataPath = Path.Combine(service.GetAppAutomationRoot(appId, targetId), "deployment.json");
        var migrated = JsonSerializer.Deserialize<RunnerRuntimeDeploymentMetadata>(File.ReadAllText(metadataPath))!;
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(service.GetAppSourceRoot(appId, targetId), "source.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(service.GetAppRuntimeApplicationDirectory(appId, targetId), "runtime.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyControllerBackend, "todos.py")), Is.True);
            Assert.That(File.Exists(Path.Combine(legacyControllerVenv, "marker.txt")), Is.True);
            Assert.That(migrated.ArtifactRoot, Is.EqualTo(service.GetAppRuntimeApplicationDirectory(appId, targetId)));
            Assert.That(File.Exists(Path.Combine(service.GetAppDataDirectory(appId), "user.db")), Is.True);
            Assert.That(File.Exists(Path.Combine(oldTargetRoot, "deployment.json")), Is.False);
            Assert.That(Directory.Exists(oldData), Is.False);
        });
    }

    [Test]
    public void MigrateLegacyLayout_AllowsIdenticalPartialDestinationAndRemovesLegacyCopy()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var appRoot = Path.Combine(service.AppsRoot, appId.ToString());
        var oldData = Path.Combine(appRoot, "data");
        var canonicalData = service.GetAppDataDirectory(appId);
        Directory.CreateDirectory(oldData);
        Directory.CreateDirectory(canonicalData);
        File.WriteAllText(Path.Combine(oldData, "user.db"), "same");
        File.WriteAllText(Path.Combine(canonicalData, "user.db"), "same");

        service.MigrateLegacyLayout();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(canonicalData, "user.db")), Is.EqualTo("same"));
            Assert.That(File.Exists(Path.Combine(oldData, "user.db")), Is.False);
            Assert.That(Directory.Exists(oldData), Is.False);
        });
    }

    [Test]
    public void MigrateLegacyLayout_RejectsConflictingPartialDestinationWithoutDeletingEitherCopy()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var appRoot = Path.Combine(service.AppsRoot, appId.ToString());
        var oldData = Path.Combine(appRoot, "data");
        var canonicalData = service.GetAppDataDirectory(appId);
        Directory.CreateDirectory(oldData);
        Directory.CreateDirectory(canonicalData);
        File.WriteAllText(Path.Combine(oldData, "user.db"), "legacy");
        File.WriteAllText(Path.Combine(canonicalData, "user.db"), "canonical");

        Assert.Throws<InvalidOperationException>(() => service.MigrateLegacyLayout());

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(oldData, "user.db")), Is.EqualTo("legacy"));
            Assert.That(File.ReadAllText(Path.Combine(canonicalData, "user.db")), Is.EqualTo("canonical"));
        });
    }

    [Test]
    public void MigrateLegacyLayout_RejectsReparsePointsAndPreservesLegacyTree()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var appRoot = Path.Combine(service.AppsRoot, appId.ToString());
        var oldData = Path.Combine(appRoot, "data");
        var outside = Path.Combine(Path.GetTempPath(), "runner-workspace-outside", Guid.NewGuid().ToString());
        var link = Path.Combine(oldData, "linked");
        Directory.CreateDirectory(oldData);
        Directory.CreateDirectory(outside);

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("The test environment does not allow symbolic-link creation.");
        }
        catch (IOException)
        {
            Assert.Ignore("The test environment does not support symbolic-link creation.");
        }

        Assert.Throws<InvalidOperationException>(() => service.MigrateLegacyLayout());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(oldData), Is.True);
            Assert.That(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), Is.True);
        });
    }

    [Test]
    public void LegacyScriptsLayout_MigratesApplicationStateButRetainsControllerTreeAndData()
    {
        var scriptsRoot = Path.Combine(Path.GetTempPath(), "runner-custom-scripts", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", scriptsRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var oldTargetRoot = Path.Combine(scriptsRoot, appId.ToString(), "targets", targetId.ToString());
        var oldSource = Path.Combine(oldTargetRoot, "source");
        var oldApplication = Path.Combine(oldTargetRoot, "runtime", "application");
        var oldBackend = service.GetLegacyControllerBackendDirectory(appId, targetId);
        var oldVenv = service.GetLegacyControllerVenvDirectory(appId, targetId);
        var oldData = service.GetLegacyControllerDataDirectory(appId);
        Directory.CreateDirectory(oldSource);
        Directory.CreateDirectory(oldApplication);
        Directory.CreateDirectory(oldBackend);
        Directory.CreateDirectory(oldVenv);
        Directory.CreateDirectory(oldData);
        File.WriteAllText(Path.Combine(oldSource, "source.txt"), "source");
        File.WriteAllText(Path.Combine(oldApplication, "runtime.txt"), "artifact");
        File.WriteAllText(Path.Combine(oldBackend, "todos.py"), "controller");
        File.WriteAllText(Path.Combine(oldVenv, "marker.txt"), "venv");
        File.WriteAllText(Path.Combine(oldData, "user.db"), "durable");
        File.WriteAllText(
            Path.Combine(oldTargetRoot, "deployment.json"),
            JsonSerializer.Serialize(new RunnerRuntimeDeploymentMetadata
            {
                AppId = appId.ToString(),
                DeploymentTargetId = targetId.ToString(),
                Runtime = new ApplicationRuntimeSpec(),
                ArtifactRoot = oldApplication,
                State = RuntimeReadinessState.Ready
            }));

        service.MigrateLegacyLayout();
        service.MigrateLegacyLayout();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(service.GetAppSourceRoot(appId, targetId), "source.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(service.GetAppRuntimeApplicationDirectory(appId, targetId), "runtime.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(oldBackend, "todos.py")), Is.True);
            Assert.That(File.Exists(Path.Combine(oldVenv, "marker.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(oldData, "user.db")), Is.True);
            Assert.That(File.Exists(Path.Combine(oldTargetRoot, "deployment.json")), Is.False);
            Assert.That(Directory.Exists(service.GetAppDataDirectory(appId)), Is.False);
        });
    }

    [Test]
    public void MigrateLegacyLayout_IgnoresReparsePointsInRetainedCanonicalControllerTree()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var backendRoot = service.GetLegacyControllerBackendDirectory(appId, targetId);
        var outside = Path.Combine(Path.GetTempPath(), "runner-workspace-outside", Guid.NewGuid().ToString());
        var link = Path.Combine(backendRoot, "linked");
        Directory.CreateDirectory(backendRoot);
        Directory.CreateDirectory(outside);

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("The test environment does not allow symbolic-link creation.");
        }
        catch (IOException)
        {
            Assert.Ignore("The test environment does not support symbolic-link creation.");
        }

        Assert.DoesNotThrow(() => service.MigrateLegacyLayout());
        Assert.That(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), Is.True);
    }
}
