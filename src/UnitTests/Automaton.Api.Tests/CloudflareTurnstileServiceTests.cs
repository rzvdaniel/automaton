using System.Net;
using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class CloudflareTurnstileServiceTests
{
    [Test]
    public async Task ValidateAsync_WhenDisabled_ReturnsTrueWithoutCallingCloudflare()
    {
        var service = CreateService(
            new CloudflareTurnstileOptions { Enabled = false },
            new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not call Cloudflare.")));

        var result = await service.ValidateAsync(null, null, CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ValidateAsync_WhenSecretMissingInDevelopmentAndBypassEnabled_ReturnsTrue()
    {
        var service = CreateService(
            new CloudflareTurnstileOptions
            {
                Enabled = true,
                AllowBypassWhenNotConfiguredInDevelopment = true,
                SecretKey = string.Empty
            },
            new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not call Cloudflare.")),
            environmentName: "Development");

        var result = await service.ValidateAsync("token", "127.0.0.1", CancellationToken.None);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ValidateAsync_WhenTokenMissing_ReturnsFalse()
    {
        var service = CreateService(
            new CloudflareTurnstileOptions
            {
                Enabled = true,
                SecretKey = "secret"
            },
            new StubHttpMessageHandler(_ => throw new InvalidOperationException("Should not call Cloudflare when token is missing.")));

        var result = await service.ValidateAsync(null, "127.0.0.1", CancellationToken.None);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ValidateAsync_WhenCloudflareReturnsSuccess_ReturnsTrueAndSendsRemoteIp()
    {
        string? capturedBody = null;
        var service = CreateService(
            new CloudflareTurnstileOptions
            {
                Enabled = true,
                SecretKey = "secret"
            },
            new StubHttpMessageHandler(request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
                };
            }));

        var result = await service.ValidateAsync("token-123", "203.0.113.5", CancellationToken.None);

        Assert.That(result, Is.True);
        Assert.That(capturedBody, Is.Not.Null);
        Assert.That(capturedBody, Does.Contain("secret=secret"));
        Assert.That(capturedBody, Does.Contain("response=token-123"));
        Assert.That(capturedBody, Does.Contain("remoteip=203.0.113.5"));
    }

    [Test]
    public async Task ValidateAsync_WhenCloudflareRejects_ReturnsFalse()
    {
        var service = CreateService(
            new CloudflareTurnstileOptions
            {
                Enabled = true,
                SecretKey = "secret"
            },
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":false}", Encoding.UTF8, "application/json")
            }));

        var result = await service.ValidateAsync("token-123", "203.0.113.5", CancellationToken.None);

        Assert.That(result, Is.False);
    }

    private static CloudflareTurnstileService CreateService(
        CloudflareTurnstileOptions options,
        HttpMessageHandler handler,
        string environmentName = "Production")
    {
        var httpClient = new HttpClient(handler);
        var environment = new StubWebHostEnvironment { EnvironmentName = environmentName };

        return new CloudflareTurnstileService(
            httpClient,
            Options.Create(options),
            environment,
            NullLogger<CloudflareTurnstileService>.Instance);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
