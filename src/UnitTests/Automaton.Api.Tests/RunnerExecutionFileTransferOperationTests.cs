using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

public sealed class RunnerExecutionFileTransferOperationTests
{
    [Test]
    public async Task FileTransfersUseTheCorrectQueueAndPinTheirOperationIdentity()
    {
        await using var db = CreateDb();
        var publisher = new EventPublisher();
        var operations = RunnerExecutionTestServices.Operations(db, publisher);
        var runnerId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var uploadId = Guid.NewGuid();
        var upload = new RunnerFileTransferCommand(RunnerFileTransferKinds.UploadAppFile,
            Guid.NewGuid(), "upload-session", FileName: "input.txt");

        await operations.QueueFileTransferAsync(uploadId, runnerId, ownerId, upload, CancellationToken.None);
        Assert.That(publisher.Command,
            Is.EqualTo(new RunnerExecutionRequestedEvent(uploadId, runnerId, Management: false)));
        await operations.QueueFileTransferAsync(uploadId, runnerId, ownerId, upload, CancellationToken.None);
        Assert.That(await db.RunnerExecutionOperations.CountAsync(), Is.EqualTo(1));
        Assert.ThrowsAsync<OperationIdentityConflictException>(() => operations.QueueFileTransferAsync(
            uploadId, runnerId, ownerId, upload with { FileName = "different.txt" }, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.QueueFileTransferAsync(
            uploadId, runnerId, Guid.NewGuid(), upload, CancellationToken.None));

        var metadataId = Guid.NewGuid();
        var metadata = new RunnerFileTransferCommand(RunnerFileTransferKinds.ReadMediaMetadata,
            upload.AppId, "metadata-session", RelativePath: "media/file.txt");
        await operations.QueueFileTransferAsync(metadataId, runnerId, ownerId, metadata, CancellationToken.None);
        Assert.That(publisher.Command,
            Is.EqualTo(new RunnerExecutionRequestedEvent(metadataId, runnerId, Management: true)));
    }

    [Test]
    public async Task DownloadResultMustMatchThePersistedArtifactLengthAndChecksum()
    {
        await using var fixture = await DownloadFixture.CreateAsync();
        await fixture.AssertWrongChecksumRejectedAsync();
        await fixture.AssertMatchingChecksumAcceptedAsync();
    }

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class EventPublisher : IEventPublisher
    {
        public RunnerExecutionRequestedEvent? Command { get; private set; }
        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            Command = (RunnerExecutionRequestedEvent)(object)message;
            return Task.CompletedTask;
        }
    }

    private sealed class DownloadFixture : IAsyncDisposable
    {
        private readonly string root;
        private readonly ApplicationDbContext db;
        private readonly Runner runner;
        private readonly RunnerExecutionOperationService operations;
        private readonly Guid operationId;
        private readonly byte[] bytes = [7, 8, 9];
        private readonly string artifactPath;

        private DownloadFixture(string root, ApplicationDbContext db, Runner runner,
            RunnerExecutionOperationService operations, Guid operationId, string artifactPath)
        {
            this.root = root;
            this.db = db;
            this.runner = runner;
            this.operations = operations;
            this.operationId = operationId;
            this.artifactPath = artifactPath;
        }

        public static async Task<DownloadFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "automaton-download-result-" + Guid.NewGuid().ToString("N"));
            var db = CreateDb();
            var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = Guid.NewGuid(), Name = "transfer-test", RunnerUsers = [], ApplicationProfile = "MachineManagement" };
            db.Runners.Add(runner);
            await db.SaveChangesAsync();
            var operations = RunnerExecutionTestServices.Operations(db, artifactRoot: root);
            var operationId = Guid.NewGuid();
            await operations.QueueFileTransferAsync(operationId, runner.Id, Guid.NewGuid(),
                new RunnerFileTransferCommand(RunnerFileTransferKinds.DownloadMedia,
                    Guid.NewGuid(), "media-session", RelativePath: "media/file.bin"), CancellationToken.None);
            var store = new DeploymentPackageStore(new TestWebHostEnvironment { ContentRootPath = root },
                Microsoft.Extensions.Options.Options.Create(new Automaton.Api.Config.DeploymentPackageStorageOptions()));
            var path = store.GetRunnerTransferPath(operationId, input: false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [7, 8, 9]);
            return new DownloadFixture(root, db, runner, operations, operationId, path);
        }

        public Task AssertWrongChecksumRejectedAsync()
        {
            var invalid = Result("0000000000000000000000000000000000000000000000000000000000000000");
            Assert.ThrowsAsync<ArgumentException>(() => operations.ApplyAsync(
                runner.ApiKeyId!.Value, operationId, invalid, CancellationToken.None, runnerId: runner.Id));
            return Task.CompletedTask;
        }

        public async Task AssertMatchingChecksumAcceptedAsync()
        {
            await using var content = File.OpenRead(artifactPath);
            var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(content));
            var valid = Result(hash);
            Assert.That((await operations.ApplyAsync(runner.ApiKeyId!.Value, operationId, valid,
                CancellationToken.None, runnerId: runner.Id)).Outcome, Is.EqualTo("accepted"));
        }

        private RunnerExecutionResult Result(string checksum) => new()
        {
            Outcome = "Succeeded",
            FileTransferResult = new()
            {
                Success = true, FileName = "file.bin", Length = bytes.Length, Sha256 = checksum
            }
        };

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
