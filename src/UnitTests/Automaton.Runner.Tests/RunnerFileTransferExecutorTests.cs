using System.Net;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class RunnerFileTransferExecutorTests
{
    private string workspace = null!;
    private string? previousWorkspace;

    [SetUp]
    public void SetUp()
    {
        previousWorkspace = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        workspace = Path.Combine(Path.GetTempPath(), "automaton-file-transfer-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspace);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previousWorkspace);
        if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
    }

    [Test]
    public async Task MediaUploadUsesAuthenticatedInputAndChecksPinnedLengthAndDigest()
    {
        var bytes = new byte[] { 1, 3, 5, 7 };
        using var api = CreateApi(new ArtifactApi(bytes));
        using var provider = new ServiceCollection().AddTransient<RunnerWorkspaceService>().BuildServiceProvider();
        var executor = new RunnerFileTransferExecutor(provider.GetRequiredService<IServiceScopeFactory>());
        var appId = Guid.NewGuid();
        var relativePath = "media/pictures/marker.bin";
        var command = new RunnerFileTransferCommand(RunnerFileTransferKinds.UploadMedia, appId, "session",
            RelativePath: relativePath, Length: bytes.Length, Sha256: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));

        var result = await executor.ExecuteAsync(Guid.NewGuid(), command, api, CancellationToken.None);

        var path = new RunnerWorkspaceService().ResolveAppMediaFilePath(appId, relativePath);
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo("Succeeded"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        });
    }

    [Test]
    public async Task MediaDownloadPostsFileOverAuthenticatedArtifactEndpoint()
    {
        var appId = Guid.NewGuid();
        var relativePath = "media/marker.txt";
        var expected = new byte[] { 4, 2, 1 };
        using var fixture = new TransferFixture(new ArtifactApi([]));
        await fixture.WriteMediaAsync(appId, relativePath, expected);

        var result = await fixture.ExecuteAsync(Guid.NewGuid(),
            new RunnerFileTransferCommand(RunnerFileTransferKinds.DownloadMedia, appId, "session", RelativePath: relativePath),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo("Succeeded"));
            Assert.That(fixture.Handler.UploadedBytes, Is.EqualTo(expected));
            Assert.That(result.FileTransferResult?.Length, Is.EqualTo(expected.Length));
            Assert.That(result.FileTransferResult?.Sha256,
                Is.EqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected))));
        });
    }

    [Test]
    public async Task MediaUploadRejectsChecksumMismatchWithoutReplacingDestination()
    {
        var appId = Guid.NewGuid();
        var relativePath = "media/marker.txt";
        using var fixture = new TransferFixture(new ArtifactApi([1, 2, 3]));
        var destination = await fixture.WriteMediaAsync(appId, relativePath, "original");

        Assert.ThrowsAsync<InvalidDataException>(() => fixture.ExecuteAsync(Guid.NewGuid(),
            new RunnerFileTransferCommand(RunnerFileTransferKinds.UploadMedia, appId, "session",
                RelativePath: relativePath, Length: 3, Sha256: "not-the-digest"),
            CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(destination), Is.EqualTo("original"));
    }

    private static RunnerExecutionApiClient CreateApi(ArtifactApi handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });

    private sealed class TransferFixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection()
            .AddTransient<RunnerWorkspaceService>().BuildServiceProvider();
        private readonly RunnerFileTransferExecutor executor;
        private readonly RunnerExecutionApiClient api;

        public TransferFixture(ArtifactApi handler)
        {
            Handler = handler;
            api = CreateApi(handler);
            executor = new RunnerFileTransferExecutor(services.GetRequiredService<IServiceScopeFactory>());
        }

        public ArtifactApi Handler { get; }

        public async Task<string> WriteMediaAsync(Guid appId, string relativePath, byte[] bytes)
        {
            var path = new RunnerWorkspaceService().ResolveAppMediaFilePath(appId, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }

        public async Task<string> WriteMediaAsync(Guid appId, string relativePath, string value)
        {
            var path = await WriteMediaAsync(appId, relativePath, System.Text.Encoding.UTF8.GetBytes(value));
            return path;
        }

        public Task<RunnerExecutionResult> ExecuteAsync(
            Guid operationId, RunnerFileTransferCommand command, CancellationToken token)
            => executor.ExecuteAsync(operationId, command, api, token);

        public void Dispose()
        {
            api.Dispose();
            services.Dispose();
        }
    }

    private sealed class ArtifactApi(byte[] input) : HttpMessageHandler
    {
        public byte[]? UploadedBytes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(input) };
            UploadedBytes = await request.Content!.ReadAsByteArrayAsync(token);
            return new(HttpStatusCode.OK);
        }
    }
}
