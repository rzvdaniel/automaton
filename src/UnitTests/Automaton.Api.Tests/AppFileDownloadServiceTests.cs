using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

public sealed class AppFileDownloadServiceTests
{
    [Test]
    public async Task RepeatedDownloadSessionGetsFreshOperationAfterNotFoundOrExpiredOutput()
    {
        await using var fixture = await DownloadFixture.CreateAsync();

        var firstResponse = CreateResponse();
        await fixture.Service.DownloadAsync(fixture.AppId, fixture.OwnerId, "same-session",
            firstResponse.Response, CancellationToken.None);
        var secondResponse = CreateResponse();
        await fixture.Service.DownloadAsync(fixture.AppId, fixture.OwnerId, "same-session",
            secondResponse.Response, CancellationToken.None);
        var thirdResponse = CreateResponse();
        await fixture.Service.DownloadAsync(fixture.AppId, fixture.OwnerId, "same-session",
            thirdResponse.Response, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstResponse.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
            Assert.That(secondResponse.Response.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
            Assert.That(thirdResponse.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(fixture.Transfers.OperationIds, Has.Count.EqualTo(3));
            Assert.That(fixture.Transfers.OperationIds.Distinct().ToArray(), Has.Length.EqualTo(3));
            Assert.That(fixture.Transfers.CleanedOutputIds, Is.EqualTo(fixture.Transfers.OperationIds));
            Assert.That(((MemoryStream)thirdResponse.Response.Body).ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
        });
    }

    private static DefaultHttpContext CreateResponse()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class DownloadFixture : IAsyncDisposable
    {
        private readonly ApplicationDbContext db;

        private DownloadFixture(Guid ownerId, Guid appId, ApplicationDbContext db, FakeTransferGateway transfers,
            AppFileDownloadService service)
        {
            OwnerId = ownerId;
            AppId = appId;
            this.db = db;
            Transfers = transfers;
            Service = service;
        }

        public Guid OwnerId { get; }
        public Guid AppId { get; }
        public FakeTransferGateway Transfers { get; }
        public AppFileDownloadService Service { get; }

        public static async Task<DownloadFixture> CreateAsync()
        {
            var ownerId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var runnerId = Guid.NewGuid();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            db.WebApps.Add(new WebApp
            {
                Id = appId, Name = "Download App", Summary = string.Empty, Description = string.Empty,
                Definition = "{}", DeploymentTargets = { AppDeploymentTarget.CreateDefault(appId, runnerId) },
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow
            });
            db.Runners.Add(new Runner
            {
                Id = runnerId, Name = "runner-download", LastHeartbeatAtUtc = DateTime.UtcNow,
                RunnerUsers = [new RunnerUser { RunnerId = runnerId, UserId = ownerId }]
            });
            await db.SaveChangesAsync();
            var transfers = new FakeTransferGateway();
            var service = new AppFileDownloadService(transfers,
                new AppFileRunnerResolver(new StubTargetResolver(db)),
                NullLogger<Automaton.Api.Controllers.AppFilesController>.Instance);
            return new DownloadFixture(ownerId, appId, db, transfers, service);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class StubTargetResolver(ApplicationDbContext db) : IAppDeploymentTargetResolver
    {
        public Task<AppDeploymentTarget> ResolveAsync(Guid appId, string? targetSlug, Guid userId,
            CancellationToken cancellationToken = default)
            => db.AppDeploymentTargets.Include(target => target.ExecutionRunner)
                .SingleAsync(target => target.WebAppId == appId && target.IsDefault, cancellationToken);

        public Task<AppDeploymentTarget> ResolveByIdAsync(Guid appId, Guid deploymentTargetId, Guid userId,
            CancellationToken cancellationToken = default)
            => db.AppDeploymentTargets.Include(target => target.ExecutionRunner)
                .SingleAsync(target => target.WebAppId == appId && target.Id == deploymentTargetId, cancellationToken);
    }

    private sealed class FakeTransferGateway : IRunnerFileTransferGateway
    {
        private readonly byte[] outputBytes = [9, 8, 7];
        public List<Guid> OperationIds { get; } = [];
        public List<Guid> CleanedOutputIds { get; } = [];

        public Task<(Guid OperationId, RunnerExecutionResult Result)> ExecuteAsync(Guid operationId, Guid runnerId,
            Guid ownerId, RunnerFileTransferCommand command, IFormFile? input, CancellationToken token,
            TimeSpan? timeout = null)
        {
            OperationIds.Add(operationId);
            var result = OperationIds.Count == 1
                ? new RunnerExecutionResult
                {
                    Outcome = "Failed", FileTransferResult = new RunnerFileTransferResult
                    { Success = false, StatusCode = StatusCodes.Status404NotFound, Error = "File not found." }
                }
                : new RunnerExecutionResult
                {
                    Outcome = "Succeeded", FileTransferResult = new RunnerFileTransferResult
                    {
                        Success = true, FileName = "result.bin", ContentType = "application/octet-stream",
                        Length = outputBytes.Length
                    }
                };
            return Task.FromResult((operationId, result));
        }

        public Stream OpenOutput(Guid operationId)
        {
            if (OperationIds.Count == 2)
                throw new FileNotFoundException("The saved output artifact expired.");
            return new MemoryStream(outputBytes, writable: false);
        }

        public Task QueueAsync(Guid operationId, Guid runnerId, Guid ownerId,
            RunnerFileTransferCommand command, CancellationToken token) => Task.CompletedTask;

        public Task CleanupOutputAsync(Guid operationId)
        {
            CleanedOutputIds.Add(operationId);
            return Task.CompletedTask;
        }
    }
}
