using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class PullCommandTests
{
    [Test]
    public async Task ResolveAppAsync_WhenGivenExactSlugLoadsTheAccessibleAppById()
    {
        const string appId = "11111111-1111-1111-1111-111111111111";
        var requestedPaths = new List<string>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.PathAndQuery);
            var body = request.RequestUri.PathAndQuery switch
            {
                "/api/apps" => JsonSerializer.Serialize(new[] { new { id = appId, name = "My App", publicUrl = "my-app" } }),
                $"/api/apps/{appId}" => JsonSerializer.Serialize(new
                {
                    id = appId,
                    name = "My App",
                    publicUrl = "my-app",
                    application = new { runtime = new { port = 8080 } }
                }),
                _ => throw new AssertionException($"Unexpected request path {request.RequestUri.PathAndQuery}")
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://api.example.com/")
        };
        using var client = new AutomatonApiClient(httpClient, "test-key");

        var app = await PullCommand.ResolveAppAsync(client, "MY-APP");

        Assert.Multiple(() =>
        {
            Assert.That(app.GetProperty("id").GetString(), Is.EqualTo(appId));
            Assert.That(app.GetProperty("application").GetProperty("runtime").GetProperty("port").GetInt32(), Is.EqualTo(8080));
            Assert.That(requestedPaths, Is.EqualTo(new[] { "/api/apps", $"/api/apps/{appId}" }));
        });
    }

    [Test]
    public async Task ResolveAppAsync_WhenGivenAppIdLoadsDetailsDirectly()
    {
        const string appId = "22222222-2222-2222-2222-222222222222";
        var requestedPaths = new List<string>();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedPaths.Add(request.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id = appId, name = "My App" }),
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("https://api.example.com/")
        };
        using var client = new AutomatonApiClient(httpClient, "test-key");

        var app = await PullCommand.ResolveAppAsync(client, appId);

        Assert.Multiple(() =>
        {
            Assert.That(app.GetProperty("id").GetString(), Is.EqualTo(appId));
            Assert.That(requestedPaths, Is.EqualTo(new[] { $"/api/apps/{appId}" }));
        });
    }

    [Test]
    public void ResolveAppAsync_WhenSlugIsNotAccessibleExplainsAcceptedIdentifiers()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://api.example.com/")
        };
        using var client = new AutomatonApiClient(httpClient, "test-key");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PullCommand.ResolveAppAsync(client, "not-accessible"));

        Assert.That(exception!.Message, Does.Contain("Use an app ID or exact slug"));
    }

    [Test]
    public void SelectSourceFiles_PreservesCompleteAiDefinedLayout()
    {
        var files = PullCommand.SelectSourceFiles(
        [
            "automaton.json",
            "frontend/src/main.ts",
            "server/output/app.dll",
            "packages/shared/schema.json",
            "schemas/app.graphql",
            @"server\output\app.runtimeconfig.json"
        ]);

        Assert.That(files, Does.Contain("automaton.json"));
        Assert.That(files, Does.Contain("server/output/app.dll"));
        Assert.That(files, Does.Contain("packages/shared/schema.json"));
        Assert.That(files, Does.Contain("schemas/app.graphql"));
        Assert.That(files, Does.Contain("server/output/app.runtimeconfig.json"));
    }

    [Test]
    public void SelectSourceFiles_PreservesCaseDistinctRemotePaths()
    {
        var files = PullCommand.SelectSourceFiles(["Models/User.cs", "models/User.cs"]);

        Assert.That(files, Is.EqualTo(new[] { "Models/User.cs", "models/User.cs" }));
    }

    [TestCase("../outside.txt")]
    [TestCase(@"..\outside.txt")]
    [TestCase("/absolute.txt")]
    public void ResolveTargetPath_WhenRemotePathEscapesTarget_Throws(string relativePath)
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => PullCommand.ResolveTargetPath(targetDirectory, relativePath));

        Assert.That(exception!.Message, Does.Contain("source path"));
    }

    [Test]
    public void ResolveTargetPath_WhenRemotePathIsNested_ReturnsPathInsideTarget()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        var resolved = PullCommand.ResolveTargetPath(targetDirectory, @"packages\shared\schema.json");

        Assert.That(
            resolved,
            Is.EqualTo(Path.Combine(targetDirectory, "packages", "shared", "schema.json")));
    }

    [Test]
    public void PrepareDownloads_WhenAnyPathIsInvalid_ThrowsBeforeReturningAnyDownloads()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(
            () => PullCommand.PrepareDownloads(["frontend/src/main.ts", "../outside.txt"], targetDirectory));
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void ResolveTargetPath_WhenCaseVariantSiblingEscapesTarget_Throws()
    {
        var parentDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(parentDirectory, "MyApp");

        Assert.Throws<InvalidOperationException>(
            () => PullCommand.ResolveTargetPath(targetDirectory, "../myapp/outside.txt"));
    }

    [Test]
    public void PrepareDownloads_WhenPathsCollideOnCaseInsensitiveFilesystem_Throws()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Throws<InvalidOperationException>(
                () => PullCommand.PrepareDownloads(["Models/User.cs", "models/User.cs"], targetDirectory));
            return;
        }

        var downloads = PullCommand.PrepareDownloads(["Models/User.cs", "models/User.cs"], targetDirectory);
        Assert.That(downloads, Has.Count.EqualTo(2));
    }

    [Test]
    public void PrepareDownloads_WhenDestinationParentIsReparsePoint_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(targetDirectory);
        var linkedDirectory = Path.Combine(targetDirectory, "shared");
        var outsideDirectory = Path.Combine(root, "outside");
        Directory.CreateDirectory(outsideDirectory);

        try
        {
            Directory.CreateSymbolicLink(linkedDirectory, outsideDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("The test environment does not allow symbolic links.");
            return;
        }
        catch (IOException)
        {
            Assert.Ignore("The test environment does not allow symbolic links.");
            return;
        }

        Assert.Throws<InvalidOperationException>(
            () => PullCommand.PrepareDownloads(["shared/file.txt"], targetDirectory));
    }

    [Test]
    public void StagedPull_DoesNotExposePartialProjectWhenLaterDownloadFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        var stagingDirectory = PullCommand.CreateStagingDirectory(targetDirectory);

        try
        {
            var downloads = PullCommand.PrepareDownloads(
                ["automaton.json", "frontend/index.html"],
                stagingDirectory);
            var readCount = 0;

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await PullCommand.DownloadFilesAsync(
                    downloads,
                    _ =>
                    {
                        readCount++;
                        return readCount == 1
                            ? Task.FromResult("{\"name\":\"partial\"}")
                            : Task.FromException<string>(new InvalidOperationException("404 File not found"));
                    },
                    nonInteractive: true));

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(targetDirectory), Is.False);
                Assert.That(File.Exists(Path.Combine(targetDirectory, "automaton.json")), Is.False);
                Assert.That(File.Exists(Path.Combine(stagingDirectory, "automaton.json")), Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task StagedPull_CommitsCompleteNestedProjectAfterAllDownloadsSucceed()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        var stagingDirectory = PullCommand.CreateStagingDirectory(targetDirectory);

        try
        {
            var downloads = PullCommand.PrepareDownloads(
                ["automaton.json", "frontend/src/App.ts"],
                stagingDirectory);

            await PullCommand.DownloadFilesAsync(
                downloads,
                path => Task.FromResult(path.EndsWith("App.ts", StringComparison.Ordinal)
                    ? "export const app = true;"
                    : "{}"),
                nonInteractive: true);
            PullCommand.CommitStagedProject(stagingDirectory, targetDirectory, targetExisted: false);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(targetDirectory, "automaton.json")), Is.EqualTo("{}"));
                Assert.That(File.ReadAllText(Path.Combine(targetDirectory, "frontend", "src", "App.ts")), Is.EqualTo("export const app = true;"));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RefreshPull_WhenLaterDownloadFails_LeavesExistingProjectUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(targetDirectory);
        var existingPath = Path.Combine(targetDirectory, "source", "main.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        await File.WriteAllTextAsync(existingPath, "old source");
        var localOnlyPath = Path.Combine(targetDirectory, "notes.txt");
        await File.WriteAllTextAsync(localOnlyPath, "local only");
        var downloads = PullCommand.PrepareDownloads(["source/main.txt", "source/extra.txt"], targetDirectory);
        var readCount = 0;

        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await PullCommand.StageAndCommitRefreshAsync(
                    targetDirectory,
                    downloads,
                    _ => ++readCount == 1
                        ? Task.FromResult("new source")
                        : Task.FromException<string>(new InvalidOperationException("404 File not found")),
                    nonInteractive: true));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(existingPath), Is.EqualTo("old source"));
                Assert.That(File.ReadAllText(localOnlyPath), Is.EqualTo("local only"));
                Assert.That(File.Exists(Path.Combine(targetDirectory, "source", "extra.txt")), Is.False);
                Assert.That(Directory.GetDirectories(root, "*.pulling-*"), Is.Empty);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RefreshPull_WhenDownloadsSucceed_UpdatesMatchingFilesAndPreservesLocalOnlyFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(targetDirectory);
        var existingPath = Path.Combine(targetDirectory, "source", "main.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        await File.WriteAllTextAsync(existingPath, "old source");
        var localOnlyPath = Path.Combine(targetDirectory, "notes.txt");
        await File.WriteAllTextAsync(localOnlyPath, "local only");
        var downloads = PullCommand.PrepareDownloads(["source/main.txt", "source/extra.txt"], targetDirectory);

        try
        {
            await PullCommand.StageAndCommitRefreshAsync(
                targetDirectory,
                downloads,
                path => Task.FromResult(path.EndsWith("main.txt", StringComparison.Ordinal) ? "new source" : "new remote file"),
                nonInteractive: true);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(existingPath), Is.EqualTo("new source"));
                Assert.That(File.ReadAllText(localOnlyPath), Is.EqualTo("local only"));
                Assert.That(File.ReadAllText(Path.Combine(targetDirectory, "source", "extra.txt")), Is.EqualTo("new remote file"));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void CommitStagedProject_ReplacesAnExistingEmptyTargetAsOneDirectoryTransition()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(targetDirectory);
        var stagingDirectory = PullCommand.CreateStagingDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(stagingDirectory, "automaton.json"), "{}");

        try
        {
            PullCommand.CommitStagedProject(stagingDirectory, targetDirectory, targetExisted: true);

            Assert.That(File.ReadAllText(Path.Combine(targetDirectory, "automaton.json")), Is.EqualTo("{}"));
            Assert.That(Directory.Exists(stagingDirectory), Is.False);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void CommitStagedProject_WhenTargetChangesBeforeCommit_LeavesExistingContentUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(targetDirectory);
        var stagingDirectory = PullCommand.CreateStagingDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(stagingDirectory, "automaton.json"), "staged");
        var existingPath = Path.Combine(targetDirectory, "user-file.txt");
        File.WriteAllText(existingPath, "existing");

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => PullCommand.CommitStagedProject(stagingDirectory, targetDirectory, targetExisted: true));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("remain empty"));
                Assert.That(File.ReadAllText(existingPath), Is.EqualTo("existing"));
                Assert.That(File.Exists(Path.Combine(targetDirectory, "automaton.json")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(stagingDirectory, "automaton.json")), Is.EqualTo("staged"));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
