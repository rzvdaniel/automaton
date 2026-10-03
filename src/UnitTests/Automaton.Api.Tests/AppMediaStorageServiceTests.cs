using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMediaStorageServiceTests
{
    [Test]
    public async Task ProxyReadAsync_SetsSafePublicHeadersAndStreamsArtifact()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        await using var db = await CreateAppWithRunnerAsync(ownerId, appId, runnerId);
        var transfers = new FakeTransferGateway();
        var service = CreateService(db, transfers);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var status = await service.ProxyReadAsync(appId, ownerId, "gallery/photo.png", context.Response, CancellationToken.None);

        Assert.That(status, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(context.Response.ContentType, Is.EqualTo("image/png"));
        Assert.That(context.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
        Assert.That(context.Response.Headers["Cache-Control"].ToString(), Is.EqualTo("public, max-age=3600"));
        Assert.That(context.Response.Headers["Content-Disposition"].ToString(), Is.EqualTo("inline; filename=\"photo.png\""));
        Assert.That(transfers.OutputBytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(transfers.CleanedOutputIds, Is.EqualTo(new[] { transfers.DownloadMediaOperationId }));
    }

    [Test]
    public async Task ProxyReadAsync_ReturnsRunnerMetadataStatusWithoutStartingResponse()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        await using var db = await CreateAppWithRunnerAsync(ownerId, appId, runnerId);
        var service = CreateService(db, new FakeTransferGateway { MissingMetadata = true });
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var status = await service.ProxyReadAsync(appId, ownerId, "gallery/missing.png", context.Response, CancellationToken.None);

        Assert.That(status, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(context.Response.HasStarted, Is.False);
    }

    private static AppMediaStorageService CreateService(ApplicationDbContext db, FakeTransferGateway gateway)
        => new(gateway, new AppFileRunnerResolver(new StubTargetResolver(db)),
            Options.Create(new MediaStorageOptions()), NullLogger<AppMediaStorageService>.Instance);

    private static async Task<ApplicationDbContext> CreateAppWithRunnerAsync(Guid ownerId, Guid appId, Guid runnerId)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        db.WebApps.Add(new WebApp
        {
            Id = appId, Name = "Media App", Summary = string.Empty, Description = string.Empty,
            Definition = "{}", DeploymentTargets = { AppDeploymentTarget.CreateDefault(appId, runnerId) },
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow
        });
        db.Runners.Add(new Runner
        {
            Id = runnerId, Name = "runner-1", LastHeartbeatAtUtc = DateTime.UtcNow,
            RunnerUsers = [new RunnerUser { RunnerId = runnerId, UserId = ownerId }]
        });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class StubTargetResolver(ApplicationDbContext db) : IAppDeploymentTargetResolver
    {
        public Task<AppDeploymentTarget> ResolveAsync(Guid appId, string? targetSlug, Guid userId, CancellationToken cancellationToken = default)
            => db.AppDeploymentTargets.Include(target => target.ExecutionRunner)
                .SingleAsync(target => target.WebAppId == appId && target.IsDefault, cancellationToken);
        public Task<AppDeploymentTarget> ResolveByIdAsync(Guid appId, Guid targetId, Guid userId, CancellationToken cancellationToken = default)
            => db.AppDeploymentTargets.Include(target => target.ExecutionRunner)
                .SingleAsync(target => target.WebAppId == appId && target.Id == targetId, cancellationToken);
    }

    private sealed class FakeTransferGateway : IRunnerFileTransferGateway
    {
        public bool MissingMetadata { get; init; }
        public byte[] OutputBytes { get; private set; } = [];
        public Guid DownloadMediaOperationId { get; private set; }
        public List<Guid> CleanedOutputIds { get; } = [];

        public Task<(Guid OperationId, RunnerExecutionResult Result)> ExecuteAsync(Guid operationId, Guid runnerId,
            Guid ownerId, RunnerFileTransferCommand command, IFormFile? input, CancellationToken token, TimeSpan? timeout = null)
        {
            if (command.Kind == RunnerFileTransferKinds.ReadMediaMetadata && MissingMetadata)
                return Task.FromResult((operationId, new RunnerExecutionResult
                {
                    Outcome = "Failed", FileTransferResult = new RunnerFileTransferResult
                    { Success = false, StatusCode = StatusCodes.Status404NotFound, Error = "File not found." }
                }));

            if (command.Kind == RunnerFileTransferKinds.DownloadMedia)
            {
                DownloadMediaOperationId = operationId;
                OutputBytes = [1, 2, 3];
                return Task.FromResult((operationId, new RunnerExecutionResult
                {
                    Outcome = "Succeeded", FileTransferResult = new RunnerFileTransferResult
                    {
                        Success = true, FileName = "photo.png", ContentType = "image/png", Length = OutputBytes.Length
                    }
                }));
            }

            return Task.FromResult((operationId, new RunnerExecutionResult
            {
                Outcome = "Succeeded", FileTransferResult = new RunnerFileTransferResult
                { Success = true, FileName = "photo.png", ContentType = "image/png", Length = 3 }
            }));
        }

        public Stream OpenOutput(Guid operationId) => new MemoryStream(OutputBytes, writable: false);

        public Task CleanupOutputAsync(Guid operationId)
        {
            CleanedOutputIds.Add(operationId);
            return Task.CompletedTask;
        }
    }
}
