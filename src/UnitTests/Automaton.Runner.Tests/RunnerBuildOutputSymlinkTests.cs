using System.Diagnostics;
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

        using var fixture = await BuildOutputLinkFixture.CreateAsync(linkOutputAncestor, internalLink: false);
        var result = await fixture.DeployAsync();
        fixture.AssertPrivateFileWasNotCopied(result);
    }

    [Test]
    public async Task DynamicAppBuildPreservesInternalNodeModulesBinLinkWithRelativeDependency()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Build-output link regression requires Linux symlink semantics.");
        if (!await NodeIsAvailableAsync())
            Assert.Ignore("The Node.js executable is unavailable for the relative-import regression.");

        using var fixture = await BuildOutputLinkFixture.CreateAsync(linkOutputAncestor: false, internalLink: true);
        var result = await fixture.DeployAsync();
        await fixture.AssertInternalLinkWasPreservedAndRunsAsync(result);
    }

    [Test]
    public async Task BuildOutputDirectoryCyclesAreRejected()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Build-output link regression requires Linux symlink semantics.");

        using var fixture = await BuildOutputLinkFixture.CreateAsync(
            linkOutputAncestor: false, internalLink: false, directoryCycle: true);
        var result = await fixture.DeployAsync();
        fixture.AssertDirectoryCycleWasRejected(result);
    }

    private static async Task<bool> NodeIsAvailableAsync()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
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
        private readonly bool internalLink;
        private readonly bool directoryCycle;

        private BuildOutputLinkFixture(bool linkOutputAncestor, bool internalLink, bool directoryCycle)
        {
            this.linkOutputAncestor = linkOutputAncestor;
            this.internalLink = internalLink;
            this.directoryCycle = directoryCycle;
            testWorkspace = RunnerRuntimeContainerTestWorkspace.Create();
            workspace = new RunnerWorkspaceService();
            appId = Guid.NewGuid();
            targetId = Guid.NewGuid();
            externalRoot = CreateExternalSentinel(out sentinelPath);
            packagePath = CreatePackage(appId, internalLink);
            runtime = new GenericRuntimeAcceptanceSupport.RecordingRuntimeProcessManager();
        }

        public static Task<BuildOutputLinkFixture> CreateAsync(
            bool linkOutputAncestor,
            bool internalLink,
            bool directoryCycle = false)
            => Task.FromResult(new BuildOutputLinkFixture(linkOutputAncestor, internalLink, directoryCycle));

        public async Task<DeployAppPackageResult> DeployAsync()
        {
            var deployment = new RunnerDeploymentService(workspace, runtime,
                new AppActionDeploymentService(workspace),
                new SymlinkBuildExecutor(externalRoot, linkOutputAncestor, internalLink, directoryCycle));
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

        public async Task AssertInternalLinkWasPreservedAndRunsAsync(DeployAppPackageResult result)
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(runtime.ArtifactRoot, Is.Not.Null);
            var artifactRoot = runtime.ArtifactRoot!;
            var executableLink = Path.Combine(artifactRoot, "node_modules", ".bin", "cli");
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(executableLink), Is.True);
                Assert.That(File.GetAttributes(executableLink).HasFlag(FileAttributes.ReparsePoint), Is.True);
            });

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("node")
                {
                    WorkingDirectory = artifactRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.ArgumentList.Add("node_modules/.bin/cli");
            Assert.That(process.Start(), Is.True);
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
            Assert.That(process.ExitCode, Is.Zero, await standardError);
            Assert.That((await standardOutput).Trim(), Is.EqualTo("relative dependency"));
        }

        public void AssertDirectoryCycleWasRejected(DeployAppPackageResult result)
        {
            var artifact = Path.Combine(workspace.GetAppRuntimeApplicationDirectory(appId, targetId),
                "dist", "site", "a", "loop");
            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Message, Does.Contain("cyclic directory link"));
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

        private static string CreatePackage(Guid appId, bool dynamicApp)
        {
            var path = Path.Combine(Path.GetTempPath(), "automaton-build-output-link-test", $"{Guid.NewGuid():N}.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var manifest = new AppDeploymentPackageManifest
            {
                AppId = appId.ToString(),
                Application = new ApplicationContract
                {
                    Runtime = dynamicApp
                        ? new ApplicationRuntimeSpec
                        {
                            Image = "node:20",
                            Command = new ApplicationCommandSpec
                            {
                                Executable = "node",
                                Arguments = ["node_modules/.bin/cli"],
                                WorkingDirectory = "."
                            }
                        }
                        : new ApplicationRuntimeSpec(),
                    Build = new ApplicationBuildSpec
                    {
                        Image = "example/build:1",
                        Command = new ApplicationCommandSpec { Executable = "build" },
                        OutputDirectory = dynamicApp ? null : "dist/site"
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

    private sealed class SymlinkBuildExecutor(
        string externalRoot,
        bool linkOutputAncestor,
        bool internalLink,
        bool directoryCycle) : IRunnerApplicationBuildExecutor
    {
        public Task<ApplicationBuildExecutionResult> ExecuteAsync(
            ApplicationBuildSpec build,
            ApplicationRuntimeSpec runtime,
            string workspaceRoot,
            RunnerApplicationBuildContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var dist = Path.Combine(workspaceRoot, "dist");
            if (internalLink)
            {
                var package = Path.Combine(workspaceRoot, "node_modules", "pkg", "bin");
                var library = Path.Combine(workspaceRoot, "node_modules", "pkg", "lib");
                var bin = Path.Combine(workspaceRoot, "node_modules", ".bin");
                Directory.CreateDirectory(package);
                Directory.CreateDirectory(library);
                Directory.CreateDirectory(bin);
                var executable = Path.Combine(package, "cli.js");
                File.WriteAllText(executable,
                    "const dependency = require('../lib/cli.js'); process.stdout.write(dependency);");
                File.WriteAllText(Path.Combine(library, "cli.js"), "module.exports = 'relative dependency';");
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(executable,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.CreateSymbolicLink(Path.Combine(bin, "cli"), Path.Combine("..", "pkg", "bin", "cli.js"));
            }
            else if (directoryCycle)
            {
                var loop = Path.Combine(dist, "site", "a", "loop");
                Directory.CreateDirectory(Path.GetDirectoryName(loop)!);
                Directory.CreateSymbolicLink(loop, ".");
            }
            else if (linkOutputAncestor)
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
