using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Automaton.Api.Tests;

public sealed class RunnerFileTransferStorageTests
{
    private string root = null!;

    [SetUp]
    public void SetUp() => root = Path.Combine(Path.GetTempPath(), "automaton-transfer-store-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task RunnerArtifactIsStoredAtTheOperationIdentityAndCanBeReopened()
    {
        var store = CreateStore();
        var operationId = Guid.NewGuid();
        var expected = new byte[] { 1, 2, 3, 4 };
        await using var source = new MemoryStream(expected);

        var path = await store.StoreRunnerTransferAsync(operationId, input: true, source, expected.Length, CancellationToken.None);
        await using var saved = store.OpenRunnerTransfer(operationId, input: true);

        var actual = new byte[checked((int)saved.Length)];
        await saved.ReadExactlyAsync(actual);
        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(store.GetRunnerTransferPath(operationId, input: true)));
            Assert.That(actual, Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task OutputCleanupReleasesCapacityWithoutRemovingInputOrWaitingForRetention()
    {
        var store = CreateStore();
        var operationId = Guid.NewGuid();
        await using var source = new MemoryStream([1, 2, 3]);
        await store.StoreRunnerTransferAsync(operationId, input: true, source, 3, CancellationToken.None);
        await using var outputSource = new MemoryStream([4, 5, 6]);
        await store.StoreRunnerTransferAsync(operationId, input: false, outputSource, 3, CancellationToken.None);
        var inputPath = store.GetRunnerTransferPath(operationId, input: true);
        var outputPath = store.GetRunnerTransferPath(operationId, input: false);

        await store.TryDeleteRunnerTransferAsync(operationId, input: false);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(outputPath), Is.False);
            Assert.That(File.Exists(inputPath), Is.True);
        });
    }

    [Test]
    public async Task GatewayCleanupKeepsOutputUntilOperationIsTerminal()
    {
        await using var fixture = await CleanupFixture.CreateAsync(root);

        await fixture.Gateway.CleanupOutputAsync(fixture.OperationId);
        Assert.That(File.Exists(fixture.OutputPath), Is.True,
            "An unfinished operation may still be delivering this artifact.");

        await fixture.MarkCompletedAsync();
        await fixture.Gateway.CleanupOutputAsync(fixture.OperationId);

        Assert.That(File.Exists(fixture.OutputPath), Is.False);
    }

    [Test]
    public async Task StoreWaitsForTheSharedAppDataCapacityLock()
    {
        var appData = Path.Combine(root, "AppData");
        Directory.CreateDirectory(appData);
        var lockPath = Path.Combine(appData, ".deployment-operations.lock");
        using var heldLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var source = new MemoryStream([5, 6, 7]);

        var write = CreateStore().StoreRunnerTransferAsync(Guid.NewGuid(), input: true,
            source, 3, timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token);
        Assert.That(write.IsCompleted, Is.False);
        heldLock.Dispose();
        var path = await write;

        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void RunnerArtifactAboveBoundIsRejectedBeforeReadingTheBody()
    {
        var store = CreateStore();
        using var source = new MemoryStream();

        Assert.ThrowsAsync<InvalidDataException>(() => store.StoreRunnerTransferAsync(Guid.NewGuid(),
            input: false, source, RunnerFileTransferBounds.MaximumBytes + 1, CancellationToken.None));
        Assert.That(Directory.Exists(Path.Combine(root, "AppData", "deployment-operations")), Is.False);
    }

    private DeploymentPackageStore CreateStore()
        => new(new TestWebHostEnvironment { ContentRootPath = root },
            Options.Create(new DeploymentPackageStorageOptions()));

    private sealed class CleanupFixture(ApplicationDbContext db, Guid operationId, string outputPath,
        RunnerFileTransferGateway gateway) : IAsyncDisposable
    {
        public Guid OperationId { get; } = operationId;
        public string OutputPath { get; } = outputPath;
        public RunnerFileTransferGateway Gateway { get; } = gateway;

        public static async Task<CleanupFixture> CreateAsync(string root)
        {
            var db = CreateDatabase();
            var operationId = Guid.NewGuid();
            await AddPendingOperationAsync(db, operationId);
            var store = CreateStore(root);
            var outputPath = await StoreOutputAsync(store, operationId);
            var gateway = CreateGateway(db, store, root);
            return new CleanupFixture(db, operationId, outputPath, gateway);
        }

        public async Task MarkCompletedAsync()
        {
            var operation = await db.RunnerExecutionOperations.SingleAsync(item => item.Id == OperationId);
            operation.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        private static ApplicationDbContext CreateDatabase()
            => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

        private static async Task AddPendingOperationAsync(ApplicationDbContext db, Guid operationId)
        {
            db.RunnerExecutionOperations.Add(new RunnerExecutionOperation
            {
                Id = operationId, RunnerId = Guid.NewGuid(), RequestedByUserId = Guid.NewGuid(), ProtectedRequest = "{}"
            });
            await db.SaveChangesAsync();
        }

        private static DeploymentPackageStore CreateStore(string root)
            => new(new TestWebHostEnvironment { ContentRootPath = root },
                Options.Create(new DeploymentPackageStorageOptions()));

        private static async Task<string> StoreOutputAsync(DeploymentPackageStore store, Guid operationId)
        {
            await using var source = new MemoryStream([4, 5, 6]);
            return await store.StoreRunnerTransferAsync(operationId, input: false, source, 3,
                CancellationToken.None);
        }

        private static RunnerFileTransferGateway CreateGateway(ApplicationDbContext db,
            DeploymentPackageStore store, string root)
            => new(db, store, RunnerExecutionTestServices.Operations(db, artifactRoot: root),
                new ResourceAuthorizationService(db, null!));

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
