using System.Net;
using System.Text;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

public sealed class AutomatonApiClientCleanupTests
{
    [Test]
    public async Task ProjectRunnerAndDeletionMethodsUseScopedApiRoutes()
    {
        var requests = new List<(string Method, string Path)>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery));
            var body = request.RequestUri.AbsolutePath.EndsWith("/removal", StringComparison.Ordinal)
                ? "{\"runnerId\":\"runner-1\",\"uninstallCommand\":\"uninstall\"}"
                : "[]";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        })) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new AutomatonApiClient(http, "key");

        await client.ListProjectRunnersAsync("project-1");
        await client.AddRunnerToProjectAsync("project-1", "runner-1");
        await client.RemoveRunnerFromProjectAsync("project-1", "runner-1");
        await client.RemoveAppFromProjectAsync("project-1", "app-1");
        await client.DeleteAppAsync("app-1");
        await client.PrepareRunnerRemovalAsync("runner-1");
        await client.DeleteRunnerAsync("runner-1");

        Assert.That(requests, Is.EqualTo(new[]
        {
            ("GET", "/api/projects/project-1/runners"),
            ("POST", "/api/projects/project-1/runners/runner-1"),
            ("DELETE", "/api/projects/project-1/runners/runner-1"),
            ("DELETE", "/api/projects/project-1/apps/app-1"),
            ("DELETE", "/api/apps/app-1"),
            ("POST", "/api/runners/runner-1/removal"),
            ("DELETE", "/api/runners/runner-1?hostCleanupConfirmed=true")
        }));
    }

    [Test]
    public async Task RunnerInstallationSendsAProfileTheServerAccepts()
    {
        string? body = null;
        using var http = new HttpClient(new Handler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        })) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new AutomatonApiClient(http, "key");

        await client.CreateRunnerInstallationAsync("e2e-runner");

        var profile = System.Text.Json.JsonDocument.Parse(body!).RootElement.GetProperty("applicationProfile").GetString();
        Assert.That(Automaton.Core.Enums.RunnerApplicationProfiles.TryNormalize(profile, out _), Is.True);
        Assert.That(profile, Is.EqualTo("MachineManagement"));
        Assert.That(Automaton.Core.Enums.RunnerApplicationProfiles.Normalize("machine-management"), Is.EqualTo("MachineManagement"));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(response(request));
    }
}
