using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
    public void ConnectedFlowArtifactsUseTargetScopedGenericArtifactRoot()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var root = service.GetAppFlowArtifactsRoot(appId, targetId);
        var flow = service.ResolveAppFlowArtifactPath(appId, targetId, "flow-id.json");

        Assert.Multiple(() =>
        {
            Assert.That(root, Is.EqualTo(Path.Combine(
                Path.GetFullPath(workspaceRoot), "apps", appId.ToString(), "targets", targetId.ToString(),
                "artifacts", "current", ".automaton", "flows")));
            Assert.That(flow, Is.EqualTo(Path.Combine(root, "flow-id.json")));
            Assert.That(() => service.ResolveAppFlowArtifactPath(appId, targetId, "../escape.json"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => service.ResolveAppFlowArtifactPath(appId, targetId, "nested/../../escape.json"),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void ManagedArtifactSynchronizationCreatesCleanTargetAndReplacesRemovedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "managed-artifacts", Guid.NewGuid().ToString("N"), ".automaton", "flows");
        var firstPath = $"{Guid.NewGuid():N}.json";
        var secondPath = $"{Guid.NewGuid():N}.json";

        try
        {
            ManagedApplicationArtifactSynchronizer.Replace(
                root,
                [
                    new ApplicationManagedArtifactFile(firstPath, "first"u8.ToArray()),
                    new ApplicationManagedArtifactFile(secondPath, "second"u8.ToArray())
                ],
                static path => path);

            Assert.That(File.ReadAllText(Path.Combine(root, firstPath)), Is.EqualTo("first"));
            Assert.That(File.Exists(Path.Combine(root, secondPath)), Is.True);

            ManagedApplicationArtifactSynchronizer.Replace(
                root,
                [new ApplicationManagedArtifactFile(firstPath, "updated"u8.ToArray())],
                static path => path);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(root, firstPath)), Is.EqualTo("updated"));
                Assert.That(File.Exists(Path.Combine(root, secondPath)), Is.False);
            });
        }
        finally
        {
            var managedRoot = Directory.GetParent(root)?.Parent?.FullName;
            if (managedRoot is not null && Directory.Exists(managedRoot))
                Directory.Delete(managedRoot, recursive: true);
        }
    }

    [Test]
    public void ManagedArtifactSynchronizationRestoresPreviousSetWhenFinalRenameFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "managed-artifacts", Guid.NewGuid().ToString("N"), ".automaton", "flows");
        var path = "flow.json";

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, path), "previous");

            Assert.Throws<IOException>(() => ManagedApplicationArtifactSynchronizer.Replace(
                root,
                [new ApplicationManagedArtifactFile(path, "replacement"u8.ToArray())],
                static value => value,
                static (source, destination) =>
                {
                    if (source.Contains(".flows-staging-", StringComparison.OrdinalIgnoreCase)
                        && destination.EndsWith(Path.Combine(".automaton", "flows"), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("simulated final rename failure");
                    Directory.Move(source, destination);
                }));

            Assert.That(File.ReadAllText(Path.Combine(root, path)), Is.EqualTo("previous"));
        }
        finally
        {
            var managedRoot = Directory.GetParent(root)?.Parent?.FullName;
            if (managedRoot is not null && Directory.Exists(managedRoot))
                Directory.Delete(managedRoot, recursive: true);
        }
    }

    [Test]
    public void ManagedArtifactSynchronizationIgnoresBackupCleanupFailureAfterCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), "managed-artifacts", Guid.NewGuid().ToString("N"), ".automaton", "flows");
        var path = "flow.json";

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, path), "previous");

            Assert.DoesNotThrow(() => ManagedApplicationArtifactSynchronizer.Replace(
                root,
                [new ApplicationManagedArtifactFile(path, "replacement"u8.ToArray())],
                static value => value,
                deleteDirectory: static _ => throw new IOException("simulated backup cleanup failure")));

            Assert.That(File.ReadAllText(Path.Combine(root, path)), Is.EqualTo("replacement"));
        }
        finally
        {
            var managedRoot = Directory.GetParent(root)?.Parent?.FullName;
            if (managedRoot is not null && Directory.Exists(managedRoot))
                Directory.Delete(managedRoot, recursive: true);
        }
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
    public void PrepareApplicationStorage_RefreshesWritablePermissionsForContainerUsers()
    {
        if (OperatingSystem.IsWindows())
            return;

        var appId = Guid.NewGuid();
        var originalWorkspaceRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString("N"));
        var storage = new ApplicationStorageSpec
        {
            DataDirectory = "app-data",
            Mounts = [new ApplicationStorageMount { Name = "uploads", RelativePath = "media/uploads" }]
        };
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        try
        {
            var dataDirectory = service.ResolveApplicationDataPath(appId, storage.DataDirectory);
            var mountDirectory = service.ResolveApplicationDataPath(appId, storage.Mounts[0].RelativePath);
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(mountDirectory);
            File.SetUnixFileMode(dataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(mountDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            service.PrepareApplicationStorage(appId, storage);

            var requiredWritePermissions = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
            var dataPermissions = File.GetUnixFileMode(dataDirectory) & requiredWritePermissions;
            var mountPermissions = File.GetUnixFileMode(mountDirectory) & requiredWritePermissions;
            Assert.Multiple(() =>
            {
                Assert.That(dataPermissions, Is.EqualTo(requiredWritePermissions));
                Assert.That(mountPermissions, Is.EqualTo(requiredWritePermissions));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalWorkspaceRoot);
            if (Directory.Exists(workspaceRoot))
                Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [TestCase(Architecture.X64, 0xB0000)]
    [TestCase(Architecture.Arm64, 0x8C000)]
    public void LinuxManagedDirectoryOpenFlags_AreArchitectureSpecific(Architecture architecture, int expectedFlags)
    {
        Assert.That(LinuxManagedDirectoryPermissions.GetManagedDirectoryOpenFlags(architecture), Is.EqualTo(expectedFlags));
    }

    [TestCase(Architecture.X86)]
    [TestCase(Architecture.Arm)]
    public void LinuxManagedDirectoryOpenFlags_RejectUnsupportedArchitectures(Architecture architecture)
    {
        Assert.That(
            () => LinuxManagedDirectoryPermissions.GetManagedDirectoryOpenFlags(architecture),
            Throws.TypeOf<PlatformNotSupportedException>());
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void LinuxPermissionPreparation_ChangesVerifiedDirectoryAfterAncestorIsReplacedWithSymlink()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Handle-anchored permission preparation is Linux-specific.");

        var testRoot = Path.Combine(Path.GetTempPath(), "runner-permission-boundary", Guid.NewGuid().ToString("N"));
        var trustedRoot = Path.Combine(testRoot, "storage");
        var appDirectory = Path.Combine(trustedRoot, "app");
        var dataDirectory = Path.Combine(appDirectory, "data");
        var movedAppDirectory = Path.Combine(trustedRoot, "app-original");
        var outsideDirectory = Path.Combine(testRoot, "outside");
        var outsideDataDirectory = Path.Combine(outsideDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(outsideDataDirectory);
        File.SetUnixFileMode(dataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(outsideDataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var outsideMode = File.GetUnixFileMode(outsideDataDirectory);

        try
        {
            using (var verifiedDirectory = LinuxManagedDirectoryPermissions.OpenManagedDirectory(trustedRoot, "app/data"))
            {
                Directory.Move(appDirectory, movedAppDirectory);
                Directory.CreateSymbolicLink(appDirectory, outsideDirectory);

                LinuxManagedDirectoryPermissions.SetWritableDirectoryPermissions(verifiedDirectory);
            }

            var writePermissions = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
            Assert.Multiple(() =>
            {
                Assert.That(File.GetUnixFileMode(Path.Combine(movedAppDirectory, "data")) & writePermissions, Is.EqualTo(writePermissions));
                Assert.That(File.GetUnixFileMode(outsideDataDirectory), Is.EqualTo(outsideMode));
                Assert.That(() => LinuxManagedDirectoryPermissions.PrepareWritableDirectory(trustedRoot, "app/data"), Throws.TypeOf<IOException>());
            });

            Assert.That(File.GetUnixFileMode(outsideDataDirectory), Is.EqualTo(outsideMode));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
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



















    private static void CreatePythonVirtualEnvironmentFixture(string venvRoot, string? externalRoot = null)
    {
        var binRoot = Path.Combine(venvRoot, "bin");
        Directory.CreateDirectory(binRoot);
        File.WriteAllText(Path.Combine(venvRoot, "pyvenv.cfg"), "home = /usr/local/bin\n");
        File.WriteAllText(Path.Combine(binRoot, "python3"), "python launcher\n");

        var pythonTarget = externalRoot is null
            ? "python3"
            : Path.Combine(externalRoot, "not-runtime-content.txt");
        var pythonLink = Path.Combine(binRoot, "python");
        try
        {
            File.CreateSymbolicLink(pythonLink, pythonTarget);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("The test environment does not allow symbolic-link creation.");
        }
        catch (IOException)
        {
            Assert.Ignore("The test environment does not support symbolic-link creation.");
        }
    }
}
