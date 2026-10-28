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
    [TestCase(RunnerPurposes.Managed, "Standard")]
    [TestCase("Infrastructure", "Standard")]
    [TestCase("Infrastructure", RunnerApplicationProfiles.MachineManagement)]
    public async Task RetiredProfiles_DoNotMeasureOrSubmitStorageReports(string purpose, string profile)
    {
        var previousProfile = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE");
        var previousHostedId = Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID");
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", profile);
        Environment.SetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID", Guid.NewGuid().ToString());
        try
        {
            // A retired profile must not use the telemetry client, even when old enrollment variables remain.
            var service = new StorageTelemetryBackgroundService(null!);
            Assert.That(await service.MeasureAndReportAsync(purpose, CancellationToken.None), Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE", previousProfile);
            Environment.SetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID", previousHostedId);
        }
    }

    [Test]
    public void ReadFilesystemCapacity_ReturnsValidatedCapacityForWorkspaceFilesystem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"automaton-runner-volume-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var capacity = StorageTelemetryBackgroundService.ReadFilesystemCapacity(path);

            Assert.That(capacity.TotalBytes, Is.GreaterThan(0));
            Assert.That(capacity.AvailableBytes, Is.InRange(0, capacity.TotalBytes));
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Test]
    public async Task TelemetryClient_PostsMachineWorkspaceCapacityToAuthenticatedMachineEndpoint()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("AUTOMATON_API_KEY");
        var previousServerUrl = Environment.GetEnvironmentVariable("AUTOMATON_SERVER_URL");
        var previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        var previousUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        var isolatedAppData = Path.Combine(Path.GetTempPath(), $"automaton-runner-config-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", "test-runner-key");
        Environment.SetEnvironmentVariable("AUTOMATON_SERVER_URL", "https://api.example.test/");
        Environment.SetEnvironmentVariable("APPDATA", isolatedAppData);
        Environment.SetEnvironmentVariable("USERPROFILE", "test-user");
        try
        {
            var handler = new RecordingHttpMessageHandler();
            var storage = new FileStorageService();
            var client = new RunnerStorageTelemetryApiClient(
                new ConfigurationService(
                    Options.Create(new AppConfig { BaseUrl = "https://api.example.test/" }),
                    new ConfigurationBuilder().Build(), storage),
                storage, new SingleClientFactory(handler));
            var report = new MachineRunnerStorageTelemetry(DateTime.UtcNow, 1000, 400, true, null, "test");

            await client.ReportMachineRunnerAsync(report, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(handler.RequestUri, Is.EqualTo(new Uri("https://api.example.test/api/runner-storage/machine")));
                Assert.That(handler.ApiKey, Is.EqualTo("test-runner-key"));
                Assert.That(handler.MachineReport, Is.EqualTo(report));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOMATON_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("AUTOMATON_SERVER_URL", previousServerUrl);
            Environment.SetEnvironmentVariable("APPDATA", previousAppData);
            Environment.SetEnvironmentVariable("USERPROFILE", previousUserProfile);
            if (Directory.Exists(isolatedAppData))
                Directory.Delete(isolatedAppData, recursive: true);
        }
    }

    [Test]
    public void TelemetrySchedule_AppliesMachineInitialDelayAndJitter()
    {
        var random = new Random(42);
        for (var index = 0; index < 100; index++)
        {
            Assert.That(StorageTelemetryBackgroundService.GetInitialDelay(random).TotalMinutes,
                Is.InRange(2, 10));
            Assert.That(StorageTelemetryBackgroundService.GetNextDelay(true, random).TotalMinutes,
                Is.InRange(55, 65));
            Assert.That(StorageTelemetryBackgroundService.GetNextDelay(false, random).TotalMinutes,
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
        public MachineRunnerStorageTelemetry? MachineReport { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("X-API-Key").Single();
            if (request.RequestUri?.AbsolutePath.EndsWith("/machine", StringComparison.Ordinal) == true)
                MachineReport = await request.Content!.ReadFromJsonAsync<MachineRunnerStorageTelemetry>(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
