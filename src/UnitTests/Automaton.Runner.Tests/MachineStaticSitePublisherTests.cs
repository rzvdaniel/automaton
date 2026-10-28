using System.Text.Json;
using Automaton.Core.Models;
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
            Assert.That(config, Does.Contain($"location ^~ /{firstApp:D}/{firstOperation:D}/"));
            Assert.That(config, Does.Contain($"location ^~ /{secondApp:D}/{secondOperation:D}/"));
            Assert.That(config, Does.Not.Contain("rewrite "));
        });
    }

    [Test]
    public async Task PublishAsync_RefusesLowDiskReplacementWithoutReplacingCurrentVersion()
    {
        var availableBytes = 2L * 1024 * 1024 * 1024;
        var appId = Guid.NewGuid();
        var target = Guid.NewGuid();
        var firstOperation = Guid.NewGuid();
        var replacementOperation = Guid.NewGuid();
        var publisher = new MachineStaticSitePublisher(
            new RunnerWorkspaceService(),
            new MachineStaticServingOptions(),
            (_, _) => Task.FromResult<MachineStaticServerEndpoint?>(new("127.0.0.1", 32123)),
            (_, _) => Task.FromResult(true),
            new MachineWorkloadDiskAdmission(_ => availableBytes));
        WriteOutput(appId, target, "dist", "healthy publication");
        Assert.That(await publisher.PublishAsync(firstOperation, appId, target, "dist", CancellationToken.None), Is.Not.Null);
        WriteOutput(appId, target, "dist", "replacement publication");
        availableBytes = 0;

        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
            replacementOperation, appId, target, "dist", CancellationToken.None));

        var indexPath = Path.Combine(new RunnerWorkspaceService().GetPublishedStaticMetadataRoot(), "sites.json");
        var index = JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(await File.ReadAllTextAsync(indexPath));
        var healthyFile = Path.Combine(new RunnerWorkspaceService().GetPublishedStaticSiteVersionsRoot(appId),
            firstOperation.ToString("D"), "index.html");
        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("configured minimum is 1024 MB"));
            Assert.That(index![appId], Is.EqualTo(firstOperation));
            Assert.That(File.ReadAllText(healthyFile), Is.EqualTo("healthy publication"));
        });
    }

    [Test]
    public async Task PublishAsync_ProvidesCurrentVersionPrefixForTheTunnelRequest()
    {
        var appId = Guid.NewGuid();
        var target = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        WriteOutput(appId, target, "dist", "site content");
        var publisher = new MachineStaticSitePublisher(
            new RunnerWorkspaceService(),
            new MachineStaticServingOptions(),
            (_, _) => Task.FromResult<MachineStaticServerEndpoint?>(new("127.0.0.1", 32123)),
            (_, _) => Task.FromResult(true));

        var published = await publisher.PublishAsync(operationId, appId, target, "dist", CancellationToken.None);
        var current = await publisher.GetEndpointAsync(appId, target, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(published?.PathPrefix, Is.EqualTo($"/{appId:D}/{operationId:D}/"));
            Assert.That(current?.PathPrefix, Is.EqualTo($"/{appId:D}/{operationId:D}/"));
        });
    }

    [Test]
    public void PublishAsync_RefusesNewPublicationBelowConfiguredFreeDiskReserve()
    {
        var appId = Guid.NewGuid();
        var target = Guid.NewGuid();
        WriteOutput(appId, target, "dist", "site content");
        var containerStarted = false;
        var publisher = new MachineStaticSitePublisher(
            new RunnerWorkspaceService(),
            new MachineStaticServingOptions(),
            (_, _) =>
            {
                containerStarted = true;
                return Task.FromResult<MachineStaticServerEndpoint?>(new("127.0.0.1", 32123));
            },
            (_, _) => Task.FromResult(true),
            new MachineWorkloadDiskAdmission(_ => 0));

        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
            Guid.NewGuid(), appId, target, "dist", CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("configured minimum is 1024 MB"));
            Assert.That(containerStarted, Is.False);
            Assert.That(Directory.Exists(new RunnerWorkspaceService().GetPublishedStaticSitesRoot()), Is.False);
        });
    }

    [Test]
    public void DiskAdmissionMeasuresWorkspaceAndRunnerFilesystems()
    {
        var paths = new List<string>();
        var admission = new MachineWorkloadDiskAdmission(path =>
        {
            paths.Add(path);
            return 2L * 1024 * 1024 * 1024;
        });

        admission.EnsureAvailable(new RunnerWorkspaceService(), 1024);

        Assert.That(paths, Does.Contain(workspaceRoot));
        Assert.That(paths, Does.Contain(Path.GetPathRoot(AppContext.BaseDirectory)));
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

    [TestCase(true)]
    [TestCase(false)]
    public async Task AppCleanup_RemovesOnlyDeletedSiteAndPreservesFilesWhenReloadFails(bool reloadSucceeds)
    {
        var workspace = new RunnerWorkspaceService();
        var deletedApp = Guid.NewGuid();
        var siblingApp = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var reloadAllowed = true;
        var publisher = new MachineStaticSitePublisher(workspace, new MachineStaticServingOptions(),
            (_, _) => Task.FromResult<MachineStaticServerEndpoint?>(new("127.0.0.1", 32123)),
            (_, _) => Task.FromResult(reloadAllowed));
        WriteOutput(deletedApp, targetId, "dist", "deleted site");
        WriteOutput(siblingApp, targetId, "dist", "sibling site");
        await publisher.PublishAsync(Guid.NewGuid(), deletedApp, targetId, "dist", CancellationToken.None);
        await publisher.PublishAsync(Guid.NewGuid(), siblingApp, targetId, "dist", CancellationToken.None);
        var data = workspace.GetAppDataDirectory(deletedApp);
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "tasks.json"), "persistent tasks");
        var command = CreateCleanupExecutor(workspace, publisher);
        reloadAllowed = reloadSucceeds;

        if (reloadSucceeds)
        {
            await DeleteAppAsync(command, deletedApp, targetId);
            // Repeated cleanup must succeed even after the app's files have gone.
            await DeleteAppAsync(command, deletedApp, targetId);
        }
        else
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => DeleteAppAsync(command, deletedApp, targetId));
        }

        var sites = await ReadPublishedSitesAsync(workspace);
        Assert.That(sites.ContainsKey(siblingApp), Is.True);
        Assert.That(sites.ContainsKey(deletedApp), Is.EqualTo(!reloadSucceeds));
        var config = await File.ReadAllTextAsync(Path.Combine(workspace.GetPublishedStaticMetadataRoot(), "nginx", "default.conf"));
        Assert.That(config.Contains(deletedApp.ToString("D")), Is.EqualTo(!reloadSucceeds));
        Assert.That(config, Does.Contain(siblingApp.ToString("D")));
        Assert.That(Directory.Exists(workspace.GetPublishedStaticSiteVersionsRoot(siblingApp)), Is.True);
        Assert.That(Directory.Exists(data), Is.EqualTo(!reloadSucceeds));
    }

    private static AppActionCommandExecutor CreateCleanupExecutor(RunnerWorkspaceService workspace, MachineStaticSitePublisher publisher)
        => new(null!, null!, new RunnerDeploymentService(workspace, new CleanupRuntime(), new AppActionDeploymentService(workspace)),
            publisher, new MachineApplicationStorageCleanup((_, _) => Task.FromResult(
                new InfrastructureCommandResponse { Success = true })));

    private static async Task DeleteAppAsync(AppActionCommandExecutor command, Guid appId, Guid targetId)
        => await command.ExecuteAsync(AppActionCommand.Create("cleanup-app-resources",
            new CleanupAppDeploymentTargetRequest(appId, targetId, true)), CancellationToken.None);

    private static async Task<Dictionary<Guid, Guid>> ReadPublishedSitesAsync(RunnerWorkspaceService workspace)
        => JsonSerializer.Deserialize<Dictionary<Guid, Guid>>(await File.ReadAllTextAsync(
            Path.Combine(workspace.GetPublishedStaticMetadataRoot(), "sites.json")))!;

    private sealed class CleanupRuntime : IAppRuntimeProcessManager
    {
        public Task StopAsync(Guid appId, Guid deploymentTargetId) => Task.CompletedTask;
        public Task<RuntimeApplyResult> ApplyAsync(Guid appId, Guid deploymentTargetId, ApplicationContract contract,
            CancellationToken cancellationToken = default, string? artifactRoot = null,
            Guid? candidateId = null, Guid? deploymentAttemptId = null)
            => throw new NotSupportedException();
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
