using Automaton.Runner.Console.Services;
using NUnit.Framework;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
[NonParallelizable]
public class StorageMeasurementTests
{
    [Test]
    public async Task TelemetryClient_PostsAuthenticatedReportToConfiguredApi()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        var previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        var previousUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var isolatedAppData = Path.Combine(Path.GetTempPath(), $"automaton-runner-config-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "test-runner-key");
        Environment.SetEnvironmentVariable("APPDATA", isolatedAppData);
        Environment.SetEnvironmentVariable("USERPROFILE", "test-user");
        try
        {
            var handler = new RecordingHttpMessageHandler();
            var storage = new FileStorageService();
            var client = new RunnerStorageTelemetryApiClient(
                new ConfigurationService(
                    Options.Create(new AppConfig { BaseUrl = "https://api.example.test/" }),
                    new ConfigurationBuilder().Build(),
                    storage),
                storage,
                new SingleClientFactory(handler));
            var report = new HostedRunnerStorageTelemetry(
                Guid.NewGuid(), DateTime.UtcNow, 1, 2, 3, 4, 9, 10, 11, true, null, "test");

            await client.ReportHostedRunnerAsync(report, CancellationToken.None);

            Assert.That(handler.RequestUri, Is.EqualTo(new Uri("https://api.example.test/api/runner-storage/hosted")));
            Assert.That(handler.ApiKey, Is.EqualTo("test-runner-key"));
            Assert.That(handler.Report, Is.Not.Null);
            Assert.That(handler.Report!.HostedRunnerId, Is.EqualTo(report.HostedRunnerId));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("APPDATA", previousAppData);
            Environment.SetEnvironmentVariable("USERPROFILE", previousUserProfile);
            if (Directory.Exists(isolatedAppData))
                Directory.Delete(isolatedAppData, recursive: true);
        }
    }

    [Test]
    public async Task MissingDirectory_IsSafelyMeasuredAsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.That(await StorageMeasurement.MeasureDirectoryAsync(path, default), Is.Zero);
    }

    [Test]
    public async Task ManagedDirectory_SumsRegularFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"automaton-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(path, "child"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(path, "one.bin"), new byte[17]);
            await File.WriteAllBytesAsync(Path.Combine(path, "child", "two.bin"), new byte[23]);
            Assert.That(await StorageMeasurement.MeasureDirectoryAsync(path, default), Is.EqualTo(40));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Test]
    public void CancelledTraversal_ReportsCancellation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"automaton-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(() =>
                StorageMeasurement.MeasureDirectoryAsync(path, cancellation.Token));
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Test]
    public void TelemetrySchedule_AppliesPurposeSpecificInitialDelayAndJitter()
    {
        var random = new Random(42);
        for (var index = 0; index < 100; index++)
        {
            Assert.That(StorageTelemetryBackgroundService.GetInitialDelay(RunnerPurposes.Managed, random).TotalMinutes,
                Is.InRange(2, 10));
            Assert.That(StorageTelemetryBackgroundService.GetInitialDelay(RunnerPurposes.Infrastructure, random).TotalMinutes,
                Is.InRange(1, 5));
            Assert.That(StorageTelemetryBackgroundService.GetNextDelay(RunnerPurposes.Managed, true, random).TotalMinutes,
                Is.InRange(55, 65));
            Assert.That(StorageTelemetryBackgroundService.GetNextDelay(RunnerPurposes.Managed, false, random).TotalMinutes,
                Is.InRange(10, 15));
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public HostedRunnerStorageTelemetry? Report { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("X-API-Key").Single();
            Report = await request.Content!.ReadFromJsonAsync<HostedRunnerStorageTelemetry>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
