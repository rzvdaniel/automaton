using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
using Automaton.Api.Config;
using Automaton.Api.Services;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class PublishedAppStorageServiceTests
{
    private static readonly MethodInfo ValidateIndexHtmlMethod =
        typeof(PublishedAppStorageService).GetMethod("ValidatePortableIndexHtml", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(PublishedAppStorageService), "ValidatePortableIndexHtml");

    private static readonly MethodInfo ValidateManifestMethod =
        typeof(PublishedAppStorageService).GetMethod("ValidatePortableManifestAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(PublishedAppStorageService), "ValidatePortableManifestAsync");

    [Test]
    public void ValidatePortableIndexHtml_AllowsRelativeAndExternalReferences()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <link rel="stylesheet" href="./assets/app.css">
                <link rel="manifest" href="manifest.webmanifest">
              </head>
              <body>
                <img src="assets/logo.png">
                <script type="module" src="./assets/app.js"></script>
                <a href="https://example.com">External</a>
              </body>
            </html>
            """;

        Assert.DoesNotThrow(() => InvokeValidateIndexHtml(html));
    }

    [Test]
    public void ValidatePortableIndexHtml_RejectsRootRelativeReferences()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <link rel="stylesheet" href="/assets/app.css">
              </head>
              <body>
                <a href="/">Home</a>
              </body>
            </html>
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => InvokeValidateIndexHtml(html));

        Assert.That(ex!.Message, Does.Contain("root-relative local URLs"));
        Assert.That(ex.Message, Does.Contain("base: './'"));
    }

    [Test]
    public void ValidatePortableIndexHtml_RejectsExistingAutomatonRuntime()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <script src="./automaton-config.js"></script>
              </head>
              <body></body>
            </html>
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => InvokeValidateIndexHtml(html));

        Assert.That(ex!.Message, Does.Contain("must not include Automaton runtime scripts"));
    }

    [Test]
    public async Task ValidatePortableManifestAsync_RejectsRootRelativeUrls()
    {
        var manifestPath = Path.Combine(Path.GetTempPath(), "automaton-published-app-tests", Guid.NewGuid().ToString("N"), "manifest.webmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, """
            {
              "name": "Bad App",
              "start_url": "/",
              "scope": "/",
              "icons": [
                { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" }
              ]
            }
            """);

        try
        {
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await InvokeValidateManifestAsync(manifestPath));

            Assert.That(ex!.Message, Does.Contain("manifest.webmanifest contains root-relative URLs"));
            Assert.That(ex.Message, Does.Contain("start_url"));
            Assert.That(ex.Message, Does.Contain("scope"));
            Assert.That(ex.Message, Does.Contain("src"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(manifestPath)!, recursive: true);
        }
    }

    [Test]
    public void BuildPublicUrl_PrefixesAppsNamespace()
    {
        var service = CreateService(new PublishedAppsConfig
        {
            PublicBaseUrl = "https://studio.automaton.monster"
        });

        var result = service.BuildPublicUrl("my-cool-app");

        Assert.That(result, Is.EqualTo("https://studio.automaton.monster/apps/my-cool-app/"));
    }

    [Test]
    public void BuildPublicUrl_UsesRootSlugOnDedicatedAppsHost()
    {
        var service = CreateService(new PublishedAppsConfig
        {
            PublicBaseUrl = "https://apps.automaton.monster"
        });

        var result = service.BuildPublicUrl("my-cool-app");

        Assert.That(result, Is.EqualTo("https://apps.automaton.monster/my-cool-app/"));
    }

    [Test]
    public void BuildPublicUrl_PrefixesAppsNamespaceOnLocalhost()
    {
        var service = CreateService(new PublishedAppsConfig
        {
            PublicBaseUrl = "https://localhost:7091"
        });

        var result = service.BuildPublicUrl("my-cool-app");

        Assert.That(result, Is.EqualTo("https://localhost:7091/apps/my-cool-app/"));
    }

    private static void InvokeValidateIndexHtml(string html)
    {
        try
        {
            ValidateIndexHtmlMethod.Invoke(null, [html]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static async Task InvokeValidateManifestAsync(string manifestPath)
    {
        var task = (Task)ValidateManifestMethod.Invoke(null, [manifestPath, CancellationToken.None])!;
        await task;
    }

    private static PublishedAppStorageService CreateService(PublishedAppsConfig? config = null)
    {
        return new PublishedAppStorageService(
            new StubWebHostEnvironment(),
            null!,
            Options.Create(config ?? new PublishedAppsConfig()));
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
