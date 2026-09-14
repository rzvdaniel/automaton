using System.Net;
using System.Net.Sockets;
using System.Text;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class AutomatonApiClientTargetTests
{
    [TestCase(null, "api/apps/app-1/deploy-package?clean=true")]
    [TestCase("server eu", "api/apps/app-1/deploy-package?clean=true&targetSlug=server%20eu")]
    public async Task DeployPackageAsync_UsesOptionalTargetSlug(
        string? targetSlug,
        string expectedPath)
    {
        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            var response = request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent(
                        "{\"operationId\":\"11111111-1111-1111-1111-111111111111\",\"status\":\"Pending\"}",
                        Encoding.UTF8,
                        "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"operationId\":\"11111111-1111-1111-1111-111111111111\",\"status\":\"Succeeded\",\"result\":{\"success\":true}}",
                    Encoding.UTF8,
                    "application/json")
            };
            return response;
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        using var client = new AutomatonApiClient(httpClient, "ak_test");
        await using var package = new MemoryStream([1, 2, 3]);

        await client.DeployPackageAsync(
            "app-1",
            package,
            clean: true,
            targetSlug: targetSlug);

        Assert.That(
            requests[0].PathAndQuery.TrimStart('/'),
            Is.EqualTo(expectedPath));
        Assert.That(requests[1].PathAndQuery, Is.EqualTo("/api/apps/app-1/deploy-operations/11111111-1111-1111-1111-111111111111"));
    }

    [Test]
    public void DeployPackageAsync_ReportsSafeTransportDiagnosticsAndCorrelationId()
    {
        string? correlationId = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            correlationId = request.Headers.GetValues("X-Automaton-Correlation-Id").Single();
            throw new HttpRequestException("send failed", new SocketException(10054));
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        using var client = new AutomatonApiClient(httpClient, "ak_secret-that-must-not-appear");
        using var package = new MemoryStream([1, 2, 3]);

        var exception = Assert.ThrowsAsync<CliHttpRequestException>(async () =>
            await client.DeployPackageAsync("app-1", package));

        Assert.That(correlationId, Does.Match("^[0-9a-f]{32}$"));
        Assert.That(exception!.Message, Does.Contain("Phase: deploy-package HTTP upload"));
        Assert.That(exception.Message, Does.Contain("Request host/path: api.example.test/api/apps/app-1/deploy-package"));
        Assert.That(exception.Message, Does.Contain("Package bytes: 3"));
        Assert.That(exception.Message, Does.Contain("ConnectionError"));
        Assert.That(exception.Message, Does.Contain($"Correlation ID: {correlationId}"));
        Assert.That(exception.Message, Does.Not.Contain("ak_secret"));
    }

    [TestCase(true, "api/apps/app-1/targets/target-1/privileged-host-access")]
    [TestCase(false, "api/apps/app-1/targets/target-1/privileged-host-access")]
    public async Task PrivilegedHostAccessActions_UseTargetEndpoints(
        bool approve,
        string expectedPath)
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        using var client = new AutomatonApiClient(httpClient, "ak_test");

        if (approve)
        {
            await client.ApproveDeploymentTargetPrivilegedHostAccessAsync("app-1", "target-1");
        }
        else
        {
            await client.RevokeDeploymentTargetPrivilegedHostAccessAsync("app-1", "target-1");
        }

        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Method, Is.EqualTo(approve ? HttpMethod.Post : HttpMethod.Delete));
        Assert.That(capturedRequest.RequestUri!.PathAndQuery.TrimStart('/'), Is.EqualTo(expectedPath));
        if (approve)
        {
            Assert.That(capturedRequest.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), Is.EqualTo("{}"));
        }
    }

    [TestCase("backend/server.py", "api/apps/app-1/filesystem/source/backend/server.py")]
    [TestCase("folder with space/file#.txt", "api/apps/app-1/filesystem/source/folder%20with%20space/file%23.txt")]
    public async Task ReadScopedFileAsync_PreservesCatchAllPathHierarchy(
        string path,
        string expectedPath)
    {
        Uri? requestUri = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true,\"content\":\"file\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        using var client = new AutomatonApiClient(httpClient, "ak_test");

        var content = await client.ReadScopedFileAsync("app-1", "source", path);

        Assert.Multiple(() =>
        {
            Assert.That(content, Is.EqualTo("file"));
            Assert.That(requestUri!.PathAndQuery.TrimStart('/'), Is.EqualTo(expectedPath));
        });
    }

    [Test]
    public async Task CreateDeploymentTargetAsync_AllowsAnUnassignedTarget()
    {
        string? payload = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"id\":\"target-1\",\"name\":\"canary\",\"slug\":\"canary\",\"isDefault\":false,\"status\":\"Unassigned\",\"legacyControllerRuntimeDeployed\":false,\"runner\":null}",
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://api.example.test/")
        };
        using var client = new AutomatonApiClient(httpClient, "ak_test");

        var target = await client.CreateDeploymentTargetAsync("app-1", "canary", "canary", null);

        Assert.Multiple(() =>
        {
            Assert.That(target.Runner, Is.Null);
            Assert.That(payload, Does.Not.Contain("runnerId"));
        });
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
