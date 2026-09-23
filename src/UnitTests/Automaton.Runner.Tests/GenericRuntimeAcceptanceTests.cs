using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public sealed class GenericRuntimeAcceptanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string workspaceRoot = null!;
    private string? originalWorkspaceRoot;
    private string? originalScriptsRoot;
    private string? originalPortStart;
    private string? originalPortEnd;

    [SetUp]
    public void SetUp()
    {
        originalWorkspaceRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        originalScriptsRoot = Environment.GetEnvironmentVariable("RUNNER_SCRIPTS_DIR");
        originalPortStart = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START");
        originalPortEnd = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END");
        workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "a7",
            Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", null);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START", "24000");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END", "24999");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", originalWorkspaceRoot);
        Environment.SetEnvironmentVariable("RUNNER_SCRIPTS_DIR", originalScriptsRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_START", originalPortStart);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_PORT_END", originalPortEnd);
        if (Directory.Exists(workspaceRoot))
            Directory.Delete(workspaceRoot, recursive: true);
    }

    [Test]
    public async Task DifferentStacks_UseOneGenericBuildRuntimeReadinessAndGatewayPath()
    {
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var processManager = CreateProcessManager(workspace, registry);
        var deployment = CreateDeployment(workspace, processManager);
        await using var gateway = await StartGatewayAsync(registry);

        var fixtures = new[]
        {
            AcceptanceFixture.AspNetCore,
            AcceptanceFixture.Node,
            AcceptanceFixture.Python
        };

        for (var index = 0; index < fixtures.Length; index++)
        {
            var fixture = fixtures[index];
            var appId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var packagePath = CreatePackage(fixture, appId, $"{fixture.Name}-v1", 0);

            try
            {
                var result = await deployment.DeployPackage(new DeployAppPackageRequest
                {
                    AppId = appId,
                    DeploymentTargetId = targetId,
                    PackagePath = packagePath,
                    Clean = true
                });

                Assert.That(
                    result.Success,
                    Is.True,
                    result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Logs ?? []));
                var metadata = await ReadMetadataAsync(workspace, appId, targetId);
                var body = await gateway.GetTextAsync(appId, targetId, "/hello?from=stage7");
                var artifactRoot = metadata.ArtifactRoot!;

                Assert.Multiple(() =>
                {
                    Assert.That(result.Success, Is.True, result.Message);
                    Assert.That(result.SourceFilesDeployed, Is.GreaterThan(0));
                    Assert.That(result.RuntimeFilesDeployed, Is.GreaterThan(0));
                    Assert.That(metadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
                    Assert.That(metadata.ProcessId, Is.Not.Null);
                    Assert.That(body, Does.Contain($"{fixture.Name}:{fixture.Name}-v1:/hello?from=stage7"));
                    Assert.That(body, Does.Contain(workspace.GetAppDataDirectory(appId)));
                    Assert.That(File.Exists(Path.Combine(artifactRoot, fixture.BuiltArtifact)), Is.True);
                });
            }
            finally
            {
                await processManager.StopAsync(appId, targetId);
                DeleteFileIfExists(packagePath);
            }
        }
    }

    [Test]
    public async Task NodeReplacement_LeavesV1CurrentUntilV2ReadinessThenRoutesV2()
    {
        var fixture = AcceptanceFixture.Node;
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var processManager = CreateProcessManager(workspace, registry);
        var deployment = CreateDeployment(workspace, processManager);
        await using var gateway = await StartGatewayAsync(registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var firstPackage = CreatePackage(fixture, appId, "v1", 0);
        var replacementPackage = CreatePackage(fixture, appId, "v2", 250);
        Task<DeployAppPackageResult>? replacementTask = null;

        try
        {
            var firstResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = firstPackage,
                Clean = true
            });
            Assert.That(firstResult.Success, Is.True, firstResult.Message);

            var metadataPath = workspace.GetAppAutomationRoot(appId, targetId);
            var originalMetadata = await ReadMetadataAsync(workspace, appId, targetId);
            replacementTask = deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = replacementPackage,
                Clean = true
            });

            await WaitForCandidateAsync(metadataPath);
            var duringReplacement = await gateway.GetTextAsync(appId, targetId, "/hello");
            var replacementResult = await replacementTask;
            var afterReplacement = await gateway.GetTextAsync(appId, targetId, "/hello");
            var finalMetadata = await ReadMetadataAsync(workspace, appId, targetId);

            Assert.Multiple(() =>
            {
                Assert.That(duringReplacement, Does.Contain("node:v1:/hello"));
                Assert.That(replacementResult.Success, Is.True, replacementResult.Message);
                Assert.That(afterReplacement, Does.Contain("node:v2:/hello"));
                Assert.That(finalMetadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
                Assert.That(finalMetadata.Candidate, Is.Null);
                Assert.That(finalMetadata.ArtifactRoot, Is.Not.EqualTo(originalMetadata.ArtifactRoot));
            });
        }
        finally
        {
            if (replacementTask is not null && !replacementTask.IsCompleted)
                await replacementTask;
            await processManager.StopAsync(appId, targetId);
            DeleteFileIfExists(firstPackage);
            DeleteFileIfExists(replacementPackage);
        }
    }

    [Test]
    public async Task NodeFailedReplacement_PreservesHealthyV1RuntimeAndRoute()
    {
        var fixture = AcceptanceFixture.Node;
        var workspace = new RunnerWorkspaceService();
        var registry = new RunnerRuntimeRegistry();
        using var processManager = CreateProcessManager(workspace, registry);
        var deployment = CreateDeployment(workspace, processManager);
        await using var gateway = await StartGatewayAsync(registry);
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var firstPackage = CreatePackage(fixture, appId, "v1", 0);
        var brokenPackage = CreatePackage(fixture, appId, "broken", 0, brokenRuntime: true);

        try
        {
            var firstResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = firstPackage,
                Clean = true
            });
            Assert.That(firstResult.Success, Is.True, firstResult.Message);

            var originalMetadata = await ReadMetadataAsync(workspace, appId, targetId);
            var failedResult = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = brokenPackage,
                Clean = true
            });
            var restoredMetadata = await ReadMetadataAsync(workspace, appId, targetId);
            var body = await gateway.GetTextAsync(appId, targetId, "/hello?after=failed");

            Assert.Multiple(() =>
            {
                Assert.That(failedResult.Success, Is.False);
                Assert.That(body, Does.Contain("node:v1:/hello?after=failed"));
                Assert.That(restoredMetadata.State, Is.EqualTo(RuntimeReadinessState.Ready));
                Assert.That(restoredMetadata.ProcessId, Is.EqualTo(originalMetadata.ProcessId));
                Assert.That(restoredMetadata.ArtifactRoot, Is.EqualTo(originalMetadata.ArtifactRoot));
                Assert.That(restoredMetadata.Candidate, Is.Null);
            });
        }
        finally
        {
            await processManager.StopAsync(appId, targetId);
            DeleteFileIfExists(firstPackage);
            DeleteFileIfExists(brokenPackage);
        }
    }

    [Test]
    public async Task NodeBuildOutput_IsPassedToTheGenericRuntimeArtifactRoot()
    {
        var workspace = new RunnerWorkspaceService();
        var processManager = new RecordingRuntimeProcessManager();
        var deployment = new RunnerDeploymentService(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace),
            new LocalApplicationBuildExecutor());
        var appId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var packagePath = CreatePackage(AcceptanceFixture.Node, appId, "artifact", 0);

        try
        {
            var result = await deployment.DeployPackage(new DeployAppPackageRequest
            {
                AppId = appId,
                DeploymentTargetId = targetId,
                PackagePath = packagePath,
                Clean = true
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message + Environment.NewLine + string.Join(Environment.NewLine, result.Logs ?? []));
                Assert.That(processManager.ArtifactRoot, Is.Not.Null);
                Assert.That(File.Exists(Path.Combine(processManager.ArtifactRoot!, "out", "server.mjs")), Is.True);
                Assert.That(File.Exists(Path.Combine(processManager.ArtifactRoot!, "out", "build-marker.txt")), Is.True);
            });

        }
        finally
        {
            DeleteFileIfExists(packagePath);
        }
    }

    [Test]
    public void GenericDeploymentBoundary_HasNoTechnologySelectorInTheCanonicalContract()
    {
        var canonicalContractTypes = new[]
        {
            typeof(ApplicationContract),
            typeof(ApplicationBuildSpec),
            typeof(ApplicationRuntimeSpec),
            typeof(ApplicationHealthSpec),
            typeof(ApplicationStorageSpec),
            typeof(ApplicationStorageMount),
            typeof(ApplicationCommandSpec)
        };
        var deploymentRequestProperties = typeof(DeployAppPackageRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var technologySelectorNames = new[] { "Language", "Framework", "RuntimeType" };

        Assert.Multiple(() =>
        {
            foreach (var contractType in canonicalContractTypes)
            {
                var properties = contractType.GetProperties().Select(property => property.Name).ToArray();
                foreach (var selectorName in technologySelectorNames)
                    Assert.That(properties, Does.Not.Contain(selectorName), $"{contractType.Name} must stay stack-neutral.");
            }

            foreach (var selectorName in technologySelectorNames)
                Assert.That(deploymentRequestProperties, Does.Not.Contain(selectorName));
            Assert.That(typeof(RunnerDeploymentService).GetMethod(nameof(RunnerDeploymentService.DeployPackage)), Is.Not.Null);
        });
    }

    private RunnerRuntimeProcessManager CreateProcessManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry)
        => new(
            workspace,
            registry,
            NullLogger<RunnerRuntimeProcessManager>.Instance);

    private static RunnerDeploymentService CreateDeployment(
        RunnerWorkspaceService workspace,
        RunnerRuntimeProcessManager processManager)
        => new(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace),
            new LocalApplicationBuildExecutor());

    private static string CreatePackage(
        AcceptanceFixture fixture,
        Guid appId,
        string version,
        int healthDelayMs,
        bool brokenRuntime = false)
    {
        var packagePath = Path.Combine(
            Path.GetTempPath(),
            "automaton-generic-runtime-acceptance",
            $"{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var contract = fixture.CreateContract(version, healthDelayMs, brokenRuntime);
        WriteEntry(
            archive,
            "manifest.json",
            JsonSerializer.Serialize(new AppDeploymentPackageManifest
            {
                AppId = appId.ToString(),
                Application = contract
            }));

        var fixtureRoot = GetFixtureRoot(fixture.DirectoryName);
        foreach (var file in Directory.GetFiles(fixtureRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(fixtureRoot, file);
            WriteFileEntry(archive, $"source/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}", file);
        }

        return packagePath;
    }

    private static string GetFixtureRoot(string directoryName)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "AcceptanceFixtures", directoryName);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Acceptance fixture was not copied to the test directory: {path}");
        return path;
    }

    private static async Task<GatewayHarness> StartGatewayAsync(RunnerRuntimeRegistry registry)
    {
        var previousPort = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT");
        var port = GetFreePort();
        Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", port.ToString());
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var gateway = new RunnerRuntimeGatewayService(
            registry,
            client,
            NullLogger<RunnerRuntimeGatewayService>.Instance);

        try
        {
            await gateway.StartAsync(CancellationToken.None);
            return new GatewayHarness(gateway, client, previousPort, port);
        }
        catch
        {
            await gateway.StopAsync(CancellationToken.None);
            client.Dispose();
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", previousPort);
            throw;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<RunnerRuntimeDeploymentMetadata> ReadMetadataAsync(
        RunnerWorkspaceService workspace,
        Guid appId,
        Guid targetId)
        => await ReadMetadataFileAsync(Path.Combine(
            workspace.GetAppAutomationRoot(appId, targetId),
            "deployment.json"));

    private static async Task<RunnerRuntimeDeploymentMetadata> ReadMetadataFileAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous);
                return await JsonSerializer.DeserializeAsync<RunnerRuntimeDeploymentMetadata>(
                    stream,
                    JsonOptions)
                    ?? throw new InvalidOperationException("Deployment metadata was empty.");
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(25);
            }
        }

        throw new IOException($"Could not read deployment metadata: {path}");
    }

    private static async Task WaitForCandidateAsync(string metadataRoot)
    {
        var path = Path.Combine(metadataRoot, "deployment.json");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(path))
            {
                RunnerRuntimeDeploymentMetadata? metadata = null;
                try
                {
                    metadata = await ReadMetadataFileAsync(path);
                }
                catch (IOException)
                {
                }
                if (metadata?.Candidate is not null)
                    return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The replacement candidate was not persisted before readiness completed.");
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteFileEntry(ZipArchive archive, string path, string sourcePath)
    {
        var entry = archive.CreateEntry(path);
        using var source = File.OpenRead(sourcePath);
        using var destination = entry.Open();
        source.CopyTo(destination);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed record AcceptanceFixture(
        string Name,
        string DirectoryName,
        string BuildExecutable,
        IReadOnlyList<string> BuildArguments,
        string RuntimeExecutable,
        IReadOnlyList<string> RuntimeArguments,
        string BuiltArtifact,
        Func<string, int, bool, ApplicationContract> ContractFactory)
    {
        public static AcceptanceFixture AspNetCore { get; } = new(
            "aspnet",
            "AspNetCore",
            "dotnet",
            ["publish", "Acceptance.csproj", "--nologo", "-c", "Release", "-o", "out"],
            "dotnet",
            ["out/Acceptance.dll"],
            Path.Combine("out", "Acceptance.dll"),
            (version, _, _) => CreateContract(
                "aspnet",
                "AspNetCore",
                "dotnet",
                ["publish", "Acceptance.csproj", "--nologo", "-c", "Release", "-o", "out"],
                "dotnet",
                ["out/Acceptance.dll"],
                Path.Combine("out", "Acceptance.dll"),
                version,
                0,
                null));

        public static AcceptanceFixture Node { get; } = new(
            "node",
            "Node",
            "node",
            ["build.mjs"],
            "node",
            ["out/server.mjs"],
            Path.Combine("out", "build-marker.txt"),
            (version, delay, broken) => CreateContract(
                "node",
                "Node",
                "node",
                ["build.mjs"],
                "node",
                broken ? ["out/missing-server.mjs"] : ["out/server.mjs"],
                Path.Combine("out", "build-marker.txt"),
                version,
                delay,
                broken ? null : null));

        public static AcceptanceFixture Python { get; } = new(
            "python",
            "Python",
            "python",
            ["build.py"],
            "python",
            ["out/server.py"],
            Path.Combine("out", "build-marker.txt"),
            (version, delay, broken) => CreateContract(
                "python",
                "Python",
                "python",
                ["build.py"],
                "python",
                broken ? ["out/missing-server.py"] : ["out/server.py"],
                Path.Combine("out", "build-marker.txt"),
                version,
                delay,
                null));

        public ApplicationContract CreateContract(string version, int healthDelayMs, bool brokenRuntime)
            => ContractFactory(version, healthDelayMs, brokenRuntime);

        private static ApplicationContract CreateContract(
            string name,
            string directoryName,
            string buildExecutable,
            IReadOnlyList<string> buildArguments,
            string runtimeExecutable,
            IReadOnlyList<string> runtimeArguments,
            string builtArtifact,
            string version,
            int healthDelayMs,
            string? unused)
            => new()
            {
                Build = new ApplicationBuildSpec
                {
                    Command = new ApplicationCommandSpec
                    {
                        Executable = buildExecutable,
                        Arguments = buildArguments.ToList(),
                        WorkingDirectory = "."
                    },
                    OutputDirectory = "out",
                    TimeoutSeconds = 120
                },
                Runtime = new ApplicationRuntimeSpec
                {
                    Image = "test/runtime:1",
                    Command = new ApplicationCommandSpec
                    {
                        Executable = runtimeExecutable,
                        Arguments = runtimeArguments.ToList(),
                        WorkingDirectory = ".",
                        Environment = new Dictionary<string, string>
                        {
                            ["ACCEPTANCE_VERSION"] = version,
                            ["ACCEPTANCE_HEALTH_DELAY_MS"] = healthDelayMs.ToString()
                        }
                    },
                    Port = GetFreePort()
                },
                Health = new ApplicationHealthSpec { Path = "/health", StartupTimeoutSeconds = 20 },
                Storage = new ApplicationStorageSpec
                {
                    DataDirectory = "data",
                    Mounts = [new ApplicationStorageMount { Name = "uploads", RelativePath = "uploads" }]
                }
            };
    }

    private sealed class GatewayHarness(
        RunnerRuntimeGatewayService gateway,
        HttpClient client,
        string? previousPort,
        int port) : IAsyncDisposable
    {
        public async Task<string> GetTextAsync(Guid appId, Guid targetId, string path)
        {
            using var response = await client.GetAsync(
                new Uri($"http://127.0.0.1:{port}/apps/{appId:D}/targets/{targetId:D}{path}"));
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await gateway.StopAsync(CancellationToken.None);
            client.Dispose();
            Environment.SetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT", previousPort);
        }
    }

    private sealed class RecordingRuntimeProcessManager : IAppRuntimeProcessManager
    {
        public string? ArtifactRoot { get; private set; }

        public Task<RuntimeApplyResult> ApplyAsync(
            Guid appId,
            Guid deploymentTargetId,
            ApplicationContract contract,
            CancellationToken cancellationToken = default,
            string? artifactRoot = null,
            Guid? candidateId = null)
        {
            ArtifactRoot = artifactRoot;
            return Task.FromResult(new RuntimeApplyResult
            {
                PromotionIrreversible = artifactRoot is not null
            });
        }

        public Task StopAsync(Guid appId, Guid deploymentTargetId)
            => Task.CompletedTask;
    }
}
