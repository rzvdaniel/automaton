using System.Text.Json;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class MachineStaticSitePublisherTests
{
    private string? previousWorkspaceRoot;
    private string? previousRunnerId;
    private string workspaceRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        previousWorkspaceRoot = Environment.GetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT");
        previousRunnerId = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_ID");
        workspaceRoot = Path.Combine(Path.GetTempPath(), "automaton-static-sites", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", workspaceRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", Guid.NewGuid().ToString("D"));
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("AUTOMATON_WORKSPACE_ROOT", previousWorkspaceRoot);
        Environment.SetEnvironmentVariable("AUTOMATON_RUNNER_ID", previousRunnerId);
        if (Directory.Exists(workspaceRoot)) Directory.Delete(workspaceRoot, recursive: true);
    }

    [Test]
    public async Task PublishAsync_SeparatesSitesAndRetainsWorkingVersionWhenReplacementFails()
    {
        var firstApp = Guid.NewGuid();
        var secondApp = Guid.NewGuid();
        var target = Guid.NewGuid();
        var firstOperation = Guid.NewGuid();
        var secondOperation = Guid.NewGuid();
        var failedOperation = Guid.NewGuid();
        var ensureCalls = 0;
        var settings = new MachineStaticServingOptions();
        var publisher = new MachineStaticSitePublisher(
            new RunnerWorkspaceService(),
            settings,
            (_, _) => Task.FromResult(++ensureCalls == 3
                ? null
                : new MachineStaticServerEndpoint("127.0.0.1", 32123)),
            (_, _) => Task.FromResult(true));
        WriteOutput(firstApp, target, "dist", "first site v1");
        WriteOutput(secondApp, target, "dist", "second site");

        Assert.That(await publisher.PublishAsync(firstOperation, firstApp, target, "dist", CancellationToken.None),
            Is.Not.Null);
        Assert.That(await publisher.PublishAsync(secondOperation, secondApp, target, "dist", CancellationToken.None),
            Is.Not.Null);

        WriteOutput(firstApp, target, "dist", "first site v2");
        var replacement = await publisher.PublishAsync(
            failedOperation, firstApp, target, "dist", CancellationToken.None);

        var metadataRoot = new RunnerWorkspaceService().GetPublishedStaticMetadataRoot();
        var published = JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(
            await File.ReadAllTextAsync(Path.Combine(metadataRoot, "sites.json")));
        var firstPublishedFile = Path.Combine(
            new RunnerWorkspaceService().GetPublishedStaticSiteVersionsRoot(firstApp),
            firstOperation.ToString("D"), "index.html");
        var secondPublishedFile = Path.Combine(
            new RunnerWorkspaceService().GetPublishedStaticSiteVersionsRoot(secondApp),
            secondOperation.ToString("D"), "index.html");
        var config = await File.ReadAllTextAsync(Path.Combine(metadataRoot, "nginx", "default.conf"));

        Assert.Multiple(() =>
        {
            Assert.That(replacement, Is.Null);
            Assert.That(published![firstApp], Is.EqualTo(firstOperation));
            Assert.That(published[secondApp], Is.EqualTo(secondOperation));
            Assert.That(File.ReadAllText(firstPublishedFile), Is.EqualTo("first site v1"));
            Assert.That(File.ReadAllText(secondPublishedFile), Is.EqualTo("second site"));
            Assert.That(config, Does.Contain($"location ^~ /{firstApp:D}/"));
            Assert.That(config, Does.Contain($"location ^~ /{secondApp:D}/"));
            Assert.That(config, Does.Contain($"/{firstApp:D}/{firstOperation:D}/"));
            Assert.That(config, Does.Contain($"/{secondApp:D}/{secondOperation:D}/"));
        });
    }

    [Test]
    public async Task PublishAsync_RejectsSymlinkToExternalFileWithoutChangingSentinel()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Symlink sentinel regression requires Linux filesystem semantics.");

        var appId = Guid.NewGuid();
        var target = Guid.NewGuid();
        var output = WriteOutput(appId, target, "dist", "site content");
        var sentinel = Path.Combine(Path.GetTempPath(), $"automaton-static-sentinel-{Guid.NewGuid():N}.json");
        const string sentinelContents = "external private file";
        await File.WriteAllTextAsync(sentinel, sentinelContents);
        try
        {
            File.CreateSymbolicLink(Path.Combine(output, "external.json"), sentinel);
            var publisher = new MachineStaticSitePublisher(
                new RunnerWorkspaceService(),
                new MachineStaticServingOptions(),
                (_, _) => Task.FromResult<MachineStaticServerEndpoint?>(new("127.0.0.1", 32123)),
                (_, _) => Task.FromResult(true));

            Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
                Guid.NewGuid(), appId, target, "dist", CancellationToken.None));
            Assert.That(await File.ReadAllTextAsync(sentinel), Is.EqualTo(sentinelContents));
            Assert.That(Directory.Exists(new RunnerWorkspaceService().GetPublishedStaticSitesRoot()), Is.False);
        }
        finally
        {
            File.Delete(sentinel);
        }
    }

    [TestCase("source.js")]
    [TestCase("secrets.json")]
    [TestCase("logs/error.txt")]
    [TestCase("private-data.json")]
    public async Task PublishAsync_RejectsPrivateFilesFromDeclaredOutput(string relativePath)
    {
        var appId = Guid.NewGuid();
        var target = Guid.NewGuid();
        var output = WriteOutput(appId, target, "dist", "site content");
        var privateFile = Path.Combine(output, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(privateFile)!);
        await File.WriteAllTextAsync(privateFile, "not public");
        var containerStarted = false;
        var publisher = new MachineStaticSitePublisher(
            new RunnerWorkspaceService(),
            new MachineStaticServingOptions(),
            (_, _) =>
            {
                containerStarted = true;
                return Task.FromResult<MachineStaticServerEndpoint?>(new("127.0.0.1", 32123));
            },
            (_, _) => Task.FromResult(true));

        Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
            Guid.NewGuid(), appId, target, "dist", CancellationToken.None));
        Assert.That(containerStarted, Is.False);
    }

    private static string WriteOutput(Guid appId, Guid targetId, string outputDirectory, string indexContents)
    {
        var root = new RunnerWorkspaceService().GetAppRuntimeApplicationDirectory(appId, targetId);
        var output = Path.Combine(root, outputDirectory);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "index.html"), indexContents);
        return output;
    }
}
