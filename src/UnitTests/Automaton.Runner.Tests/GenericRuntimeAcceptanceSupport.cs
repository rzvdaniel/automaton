using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

internal static class GenericRuntimeAcceptanceSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);    internal static RunnerRuntimeProcessManager CreateProcessManager(
        RunnerWorkspaceService workspace,
        RunnerRuntimeRegistry registry)
        => new(
            workspace,
            registry,
            NullLogger<RunnerRuntimeProcessManager>.Instance);

    internal static RunnerDeploymentService CreateDeployment(
        RunnerWorkspaceService workspace,
        RunnerRuntimeProcessManager processManager)
        => new(
            workspace,
            processManager,
            new AppActionDeploymentService(workspace),
            new LocalApplicationBuildExecutor());

    internal static string CreatePackage(
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

    internal static async Task<GatewayHarness> StartGatewayAsync(RunnerRuntimeRegistry registry)
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

    internal static async Task<RunnerRuntimeDeploymentMetadata> ReadMetadataAsync(
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

    internal static async Task WaitForCandidateAsync(string metadataRoot)
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

    internal static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    internal sealed record AcceptanceFixture(
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

    internal sealed class GatewayHarness(
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

    internal sealed class RecordingRuntimeProcessManager : IAppRuntimeProcessManager
    {
        public string? ArtifactRoot { get; private set; }

        public Task<RuntimeApplyResult> ApplyAsync(
            Guid appId,
            Guid deploymentTargetId,
            ApplicationContract contract,
            CancellationToken cancellationToken = default,
            string? artifactRoot = null,
            Guid? candidateId = null,
            Guid? deploymentAttemptId = null)
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
