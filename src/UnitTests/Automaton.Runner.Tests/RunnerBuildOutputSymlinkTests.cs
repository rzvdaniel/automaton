using System.IO.Compression;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Core.Services;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerBuildOutputSymlinkTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task BuildOutputLinksCannotCopyRunnerReadableFiles(bool linkOutputAncestor)
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Build-output link boundary regression requires Linux symlink semantics.");

        using var fixture = await BuildOutputLinkFixture.CreateAsync(linkOutputAncestor);
        var result = await fixture.DeployAsync();
        fixture.AssertPrivateFileWasNotCopied(result);
    }

    private sealed class BuildOutputLinkFixture : IDisposable
    {
        private const string PrivateContents = "private host file that must never become a public artifact";
        private readonly RunnerRuntimeContainerTestWorkspace testWorkspace;
        private readonly RunnerWorkspaceService workspace;
        private readonly Guid appId;
        private readonly Guid targetId;
        private readonly string externalRoot;
        private readonly string packagePath;
        private readonly string sentinelPath;
        private readonly GenericRuntimeAcceptanceSupport.RecordingRuntimeProcessManager runtime;
        private readonly bool linkOutputAncestor;

        private BuildOutputLinkFixture(bool linkOutputAncestor)
        {
            this.linkOutputAncestor = linkOutputAncestor;
            testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
            workspace = new RunnerWorkspaceService();
            appId = Guid.NewGuid();
            targetId = Guid.NewGuid();
            externalRoot = CreateExternalSentinel(out sentinelPath);
            packagePath = CreatePackage(appId);
            runtime = new GenericRuntimeAcceptanceSupport.RecordingRuntimeProcessManager();
        }

        public static Task<BuildOutputLinkFixture> CreateAsync(bool linkOutputAncestor)
            => Task.FromResult(new BuildOutputLinkFixture(linkOutputAncestor));

        public async Task<DeployAppPackageResult> DeployAsync()
        {
            var deployment = new RunnerDeploymentService(workspace, runtime,
                new AppActionDeploymentService(workspace),
                new SymlinkBuildExecutor(externalRoot, linkOutputAncestor));
            return await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = packagePath,
                Clean = true
            });
        }

        public void AssertPrivateFileWasNotCopied(DeployAppPackageResult result)
        {
            var artifact = Path.Combine(workspace.GetAppRuntimeApplicationDirectory(appId, targetId),
                "dist", "site", "index.html");
            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Message, Does.Contain("link"));
                Assert.That(File.Exists(artifact), Is.False);
                Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo(PrivateContents));
                Assert.That(runtime.ArtifactRoot, Is.Null);
            });
        }

        public void Dispose()
        {
            testWorkspace.Dispose();
            if (File.Exists(packagePath)) File.Delete(packagePath);
            if (Directory.Exists(externalRoot)) Directory.Delete(externalRoot, recursive: true);
        }

        private static string CreateExternalSentinel(out string sentinelPath)
        {
            var root = Path.Combine(Path.GetTempPath(), "automaton-build-output-sentinel", Guid.NewGuid().ToString("N"));
            var site = Path.Combine(root, "site");
            Directory.CreateDirectory(site);
            sentinelPath = Path.Combine(site, "index.html");
            File.WriteAllText(sentinelPath, PrivateContents);
            return root;
        }

        private static string CreatePackage(Guid appId)
        {
            var path = Path.Combine(Path.GetTempPath(), "automaton-build-output-link-test", $"{Guid.NewGuid():N}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var manifest = new AppDeploymentPackageManifest
            {
                AppId = appId.ToString(),
                Application = new ApplicationContract
                {
                    Build = new ApplicationBuildSpec
                    {
                        Image = "example/build:1",
                        Command = new ApplicationCommandSpec { Executable = "build" },
                        OutputDirectory = "dist/site"
                    }
                }
            };
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest));
            WriteEntry(archive, "source/placeholder.txt", "source fixture");
            return path;
        }

        private static void WriteEntry(ZipArchive archive, string path, string contents)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(contents);
        }
    }

    private sealed class SymlinkBuildExecutor(string externalRoot, bool linkOutputAncestor) : IRunnerApplicationBuildExecutor
    {
        public Task<ApplicationBuildExecutionResult> ExecuteAsync(
            ApplicationBuildSpec build,
            ApplicationRuntimeSpec runtime,
            string workspaceRoot,
            RunnerApplicationBuildContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var dist = Path.Combine(workspaceRoot, "dist");
            if (linkOutputAncestor)
            {
                Directory.CreateSymbolicLink(dist, externalRoot);
            }
            else
            {
                var output = Path.Combine(dist, "site");
                Directory.CreateDirectory(output);
                File.CreateSymbolicLink(Path.Combine(output, "index.html"), Path.Combine(externalRoot, "site", "index.html"));
            }
            return Task.FromResult(new ApplicationBuildExecutionResult { Succeeded = true });
        }
    }
}
