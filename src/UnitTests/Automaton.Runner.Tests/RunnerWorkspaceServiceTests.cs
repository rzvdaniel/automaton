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
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside content");

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

        var exception = Assert.Throws<InvalidOperationException>(() => service.MigrateLegacyLayout());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(oldData), Is.True);
            Assert.That(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), Is.True);
            Assert.That(exception!.Message, Does.Contain(link));
            Assert.That(exception.Message, Does.Contain("legacy migration source"));
            Assert.That(exception.Message, Does.Contain("target:"));
            Assert.That(File.Exists(Path.Combine(service.GetAppDataDirectory(appId), "linked", "secret.txt")), Is.False);
        });
    }

    [Test]
    public void MigrateLegacyLayout_MigratesPreparedActionAndOpaqueVirtualEnvironment()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        const string actionKey = "reprovision-hosted-runner";
        var oldTargetRoot = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            "apps",
            appId.ToString(),
            "targets",
            targetId.ToString());
        var oldActionRoot = Path.Combine(oldTargetRoot, "runtime", "actions", actionKey);
        var oldActionCurrent = Path.Combine(oldActionRoot, "current");
        var oldActionVersions = Path.Combine(oldActionRoot, "versions", "version-1");
        var oldActionExecutions = Path.Combine(oldActionRoot, "executions", "execution-1");
        var oldVenv = Path.Combine(oldActionCurrent, ".venv");
        var outside = Path.Combine(Path.GetTempPath(), "runner-workspace-outside", Guid.NewGuid().ToString());
        Directory.CreateDirectory(oldActionCurrent);
        Directory.CreateDirectory(oldActionVersions);
        Directory.CreateDirectory(oldActionExecutions);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(oldActionCurrent, "action.yaml"), "name: reprovision-hosted-runner\n");
        File.WriteAllText(Path.Combine(oldActionCurrent, "run.py"), "print('action')\n");
        File.WriteAllText(Path.Combine(oldActionCurrent, ".automaton-action-deployment.json"), "{}\n");
        File.WriteAllText(Path.Combine(oldActionVersions, "version.txt"), "version");
        File.WriteAllText(Path.Combine(oldActionExecutions, "execution.txt"), "execution");
        File.WriteAllText(Path.Combine(outside, "not-runtime-content.txt"), "outside");
        CreatePythonVirtualEnvironmentFixture(oldVenv, outside);

        service.MigrateLegacyLayout();

        var migratedActionRoot = service.GetAppActionRuntimeRoot(appId, targetId, actionKey);
        var migratedActionCurrent = service.GetAppActionCurrentDirectory(appId, targetId, actionKey);
        var migratedVenv = Path.Combine(migratedActionCurrent, ".venv");
        var migratedPython = Path.Combine(
            migratedVenv,
            "bin",
            "python");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(migratedActionCurrent, "action.yaml")), Is.True);
            Assert.That(File.Exists(Path.Combine(migratedActionCurrent, ".automaton-action-deployment.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(migratedActionCurrent, "run.py")), Is.True);
            Assert.That(File.Exists(Path.Combine(service.GetAppActionVersionsDirectory(appId, targetId, actionKey), "version-1", "version.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(service.GetAppActionExecutionsDirectory(appId, targetId, actionKey), "execution-1", "execution.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(migratedVenv, "pyvenv.cfg")), Is.True);
            Assert.That(File.GetAttributes(migratedPython).HasFlag(FileAttributes.ReparsePoint), Is.True);
            Assert.That(Directory.Exists(oldActionRoot), Is.False);
            Assert.That(Directory.Exists(oldVenv), Is.False);
            Assert.That(File.Exists(Path.Combine(migratedActionCurrent, "not-runtime-content.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(outside, "not-runtime-content.txt")), Is.True);
        });

        service.MigrateLegacyLayout();
        Assert.That(File.Exists(Path.Combine(migratedActionCurrent, "action.yaml")), Is.True);
        Assert.That(File.GetAttributes(migratedPython).HasFlag(FileAttributes.ReparsePoint), Is.True);
    }

    [Test]
    public void MigrateLegacyLayout_DoesNotExemptVirtualEnvironmentInsideGenericArtifact()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The regression requires a platform with supported symbolic-link creation.");

        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var oldApplication = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            "apps",
            appId.ToString(),
            "targets",
            targetId.ToString(),
            "runtime",
            "application");
        var outside = Path.Combine(Path.GetTempPath(), "runner-workspace-outside", Guid.NewGuid().ToString());
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "external.txt"), "external");
        CreatePythonVirtualEnvironmentFixture(Path.Combine(oldApplication, ".venv"), outside);

        var exception = Assert.Throws<InvalidOperationException>(() => service.MigrateLegacyLayout());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("legacy migration source"));
            Assert.That(Directory.Exists(oldApplication), Is.True);
            Assert.That(Directory.Exists(service.GetAppRuntimeApplicationDirectory(appId, targetId)), Is.False);
            Assert.That(File.Exists(Path.Combine(service.GetAppRuntimeApplicationDirectory(appId, targetId), ".venv", "bin", "python")), Is.False);
            Assert.That(File.Exists(Path.Combine(outside, "external.txt")), Is.True);
        });
    }

    [Test]
    public void MigrateLegacyLayout_RejectsUnrelatedActionReparsePoint()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("The regression requires a platform with supported symbolic-link creation.");

        var workspaceRoot = Path.Combine(Path.GetTempPath(), "runner-workspace", Guid.NewGuid().ToString());
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        const string actionKey = "send-email";
        var actionCurrent = Path.Combine(
            Path.GetFullPath(workspaceRoot),
            "apps",
            appId.ToString(),
            "targets",
            targetId.ToString(),
            "runtime",
            "actions",
            actionKey,
            "current");
        var outside = Path.Combine(Path.GetTempPath(), "runner-workspace-outside", Guid.NewGuid().ToString());
        var link = Path.Combine(actionCurrent, "unexpected-link");
        Directory.CreateDirectory(actionCurrent);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
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

        var exception = Assert.Throws<InvalidOperationException>(() => service.MigrateLegacyLayout());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(link));
            Assert.That(exception.Message, Does.Contain("legacy migration source"));
            Assert.That(Directory.Exists(service.GetAppActionCurrentDirectory(appId, targetId, actionKey)), Is.False);
            Assert.That(File.Exists(Path.Combine(service.GetAppActionCurrentDirectory(appId, targetId, actionKey), "secret.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(outside, "secret.txt")), Is.True);
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
        CreatePythonVirtualEnvironmentFixture(oldVenv);
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
            Assert.That(File.Exists(Path.Combine(oldVenv, "pyvenv.cfg")), Is.True);
            Assert.That(File.Exists(Path.Combine(oldVenv, "bin", "python")), Is.True);
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
