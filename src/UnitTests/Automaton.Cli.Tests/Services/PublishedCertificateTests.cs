using System.Net;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class PublishedCertificateTests
{
    private static readonly Uri AppUrl = new("https://example.invalid/");

    [Test]
    public async Task WaitAsync_RetriesTlsErrorAndAcceptsAuthenticatedRoute()
    {
        var attempts = 0;
        using var client = new HttpClient(new ProbeHandler(_ => ++attempts == 1
            ? throw new HttpRequestException(HttpRequestError.SecureConnectionError, "Certificate is not ready")
            : new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var ready = await PublishedCertificate.WaitAsync(
            client, AppUrl, TimeSpan.FromSeconds(5), TimeSpan.Zero, CancellationToken.None);

        Assert.That(ready, Is.True);
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task WaitAsync_ReturnsPendingWhenDeadlineExpires()
    {
        using var client = new HttpClient(new ProbeHandler(
            _ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "Certificate is not ready")));

        var ready = await PublishedCertificate.WaitAsync(
            client, AppUrl, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.That(ready, Is.False);
    }

    [Test]
    public async Task CheckAsync_ReturnsLastErrorWhenCertificateNeverArrives()
    {
        using var client = new HttpClient(new ProbeHandler(
            _ => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "Certificate is not ready")));

        var error = await PublishedCertificate.CheckAsync(
            client, AppUrl, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.That(error, Does.Contain("Certificate is not ready"));
    }

    [Test]
    public void WaitAsync_PropagatesCallerCancellation()
    {
        using var client = new HttpClient(new ProbeHandler(token => throw new OperationCanceledException(token)));
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await PublishedCertificate.WaitAsync(
            client, AppUrl, TimeSpan.FromSeconds(5), TimeSpan.Zero, caller.Token));
    }

    private sealed class ProbeHandler(Func<CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(cancellationToken));
    }
}
