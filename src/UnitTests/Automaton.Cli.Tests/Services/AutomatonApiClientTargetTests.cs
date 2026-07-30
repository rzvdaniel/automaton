using System.Net;
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
        Uri? requestUri = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"success\":true}",
                    Encoding.UTF8,
                    "application/json")
            };
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
            requestUri?.PathAndQuery.TrimStart('/'),
            Is.EqualTo(expectedPath));
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
