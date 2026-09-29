using System.Net;
using System.Security.Cryptography;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

[NonParallelizable]
public sealed class HostedRunnerDeploymentPackageDownloaderTests
{
    [Test]
    public async Task DownloadAsync_ValidatesAndStoresPackageUnderRunnerWorkspace()
    {
        var package = System.Text.Encoding.UTF8.GetBytes("bounded-package");
        var fixture = CreateFixture(package);
        try
        {
            var command = CreateCommand(package, Convert.ToHexString(SHA256.HashData(package)));
            var path = await fixture.Downloader.DownloadAsync(
                command, new Uri("https://api.test/"), "runner-key", CancellationToken.None);

            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(package));
            Assert.That(path, Does.Contain(Path.Combine("runner", "package-transfer")));
            Assert.That(fixture.Handler.ApiKey, Is.EqualTo("runner-key"));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Test]
    public async Task DownloadAsync_RejectsHashMismatchAndRemovesPartialFile()
    {
        var package = System.Text.Encoding.UTF8.GetBytes("bounded-package");
        var fixture = CreateFixture(package);
        try
        {
            var command = CreateCommand(package, "00");
            Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Downloader.DownloadAsync(
                command, new Uri("https://api.test/"), "runner-key", CancellationToken.None));
            var packageDirectory = Path.Combine(fixture.Root, "runner", "package-transfer");
            Assert.That(Directory.GetFiles(packageDirectory), Is.Empty);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [TestCase("http://api.test/package")]
    [TestCase("https://other.test/package")]
    public async Task DownloadAsync_RejectsUntrustedPackageUrl(string packageUrl)
    {
        var fixture = CreateFixture([]);
        try
        {
            var command = CreateCommand([], "00") with { PackageUrl = packageUrl };
            Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Downloader.DownloadAsync(
                command, new Uri("https://api.test/"), "runner-key", CancellationToken.None));
            Assert.That(fixture.Handler.ApiKey, Is.Null);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Test]
    public async Task ResultJournal_ReusesPersistedOutcomeAcrossRetryAttempts()
    {
        var fixture = CreateFixture([]);
        try
        {
            var journal = new HostedRunnerDeploymentResultJournal(new RunnerWorkspaceService());
            var operationId = Guid.NewGuid();
            var request = new DeployAppPackageRequest
            {
                DeploymentOperationId = operationId,
                ExecutionAttemptId = Guid.NewGuid()
            };
            var retry = new DeployAppPackageRequest
            {
                DeploymentOperationId = operationId,
                ExecutionAttemptId = Guid.NewGuid()
            };
            var expected = new DeployAppPackageResult
            {
                DeploymentTargetId = Guid.NewGuid(),
                Success = true,
                Message = "durable result"
            };

            await journal.SaveAsync(request, expected, CancellationToken.None);
            var restored = await journal.TryReadAsync(retry);

            Assert.That(restored?.Success, Is.True);
            Assert.That(restored?.Message, Is.EqualTo(expected.Message));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static HostedRunnerDeploymentCommand CreateCommand(byte[] package, string hash)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "https://api.test/package", package.Length, hash, false, DateTime.UtcNow);

    private static DownloaderFixture CreateFixture(byte[] package)
    {
        var root = Path.Combine(Path.GetTempPath(), $"runner-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var prior = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", root);
        return new DownloaderFixture(root, prior, new RecordingHandler(package));
    }

    private sealed class DownloaderFixture(string root, string? priorWorkspace, RecordingHandler handler) : IDisposable
    {
        public string Root => root;
        public RecordingHandler Handler => handler;
        public HostedRunnerDeploymentPackageDownloader Downloader { get; } = new(
            new RunnerWorkspaceService(), new SingleClientFactory(handler));

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", priorWorkspace);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(byte[] package) : HttpMessageHandler
    {
        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ApiKey = request.Headers.GetValues("X-API-Key").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(package)
            });
        }
    }
}
