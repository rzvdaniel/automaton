using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppMediaStorageServiceTests
{
    [Test]
    public async Task ProxyReadAsync_SetsSafePublicHeaders()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        await using var dbContext = await CreateAppWithRunnerAsync(ownerId, appId, runnerId);
        var service = CreateSuccessfulReadService(dbContext);

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var statusCode = await service.ProxyReadAsync(appId, ownerId, "gallery/photo.png", httpContext.Response, CancellationToken.None);

        Assert.That(statusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(httpContext.Response.ContentType, Is.EqualTo("image/png"));
        Assert.That(httpContext.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
        Assert.That(httpContext.Response.Headers["Cache-Control"].ToString(), Is.EqualTo("public, max-age=3600"));
        Assert.That(httpContext.Response.Headers["Content-Disposition"].ToString(), Is.EqualTo("inline; filename=\"photo.png\""));
    }

    [Test]
    public async Task ProxyReadAsync_ReturnsMetadataStatusWithoutStartingResponse()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();

        await using var dbContext = await CreateAppWithRunnerAsync(ownerId, appId, runnerId);
        var service = CreateMissingFileReadService(dbContext);

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var statusCode = await service.ProxyReadAsync(appId, ownerId, "gallery/missing.png", httpContext.Response, CancellationToken.None);

        Assert.That(statusCode, Is.EqualTo(StatusCodes.Status404NotFound));
        Assert.That(httpContext.Response.HasStarted, Is.False);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<ApplicationDbContext> CreateAppWithRunnerAsync(
        Guid ownerId,
        Guid appId,
        Guid runnerId)
    {
        var dbContext = CreateDbContext();
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Media App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            DeploymentTargets = { AppDeploymentTarget.CreateDefault(appId, runnerId) },
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "runner-1",
            ConnectionId = "runner-connection",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            RunnerUsers = [new RunnerUser { RunnerId = runnerId, UserId = ownerId }]
        });
        await dbContext.SaveChangesAsync();
        return dbContext;
    }

    private static AppMediaStorageService CreateSuccessfulReadService(ApplicationDbContext dbContext)
    {
        var downloadRegistry = new FileDownloadRegistry();
        var hubContext = new FakeHubContext(
            (_, relativePath) => new AppMediaFileMetadataResponse
            {
                Success = true,
                StatusCode = StatusCodes.Status200OK,
                FileName = Path.GetFileName(relativePath),
                ContentType = "image/png",
                Length = 3
            },
            request => downloadRegistry.TryWrite(new FileChunkReadyMessage
            {
                SessionId = request.SessionId,
                StatusCode = StatusCodes.Status200OK,
                ContentType = "image/png",
                FileName = "photo.png",
                Data = [1, 2, 3],
                IsLast = true
            }));
        return CreateService(dbContext, hubContext, downloadRegistry);
    }

    private static AppMediaStorageService CreateMissingFileReadService(ApplicationDbContext dbContext)
    {
        var hubContext = new FakeHubContext(
            (_, _) => new AppMediaFileMetadataResponse
            {
                Success = false,
                StatusCode = StatusCodes.Status404NotFound,
                Error = "File not found."
            },
            _ => { });
        return CreateService(dbContext, hubContext, new FileDownloadRegistry());
    }

    private static AppMediaStorageService CreateService(
        ApplicationDbContext dbContext,
        IHubContext<AutomatonHub> hubContext,
        FileDownloadRegistry downloadRegistry)
        => new(
            hubContext,
            new FileUploadRegistry(),
            downloadRegistry,
            Options.Create(new MediaStorageOptions()),
            NullLogger<AppMediaStorageService>.Instance,
            new StubTargetResolver(dbContext));

    private sealed class StubTargetResolver(ApplicationDbContext dbContext)
        : IAppDeploymentTargetResolver
    {
        public async Task<AppDeploymentTarget> ResolveAsync(
            Guid appId,
            string? targetSlug,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await dbContext.AppDeploymentTargets
                .Include(target => target.ExecutionRunner)
                .SingleAsync(
                    target => target.WebAppId == appId && target.IsDefault,
                cancellationToken);
        }

        public async Task<AppDeploymentTarget> ResolveByIdAsync(
            Guid appId,
            Guid targetId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return await dbContext.AppDeploymentTargets
                .Include(target => target.ExecutionRunner)
                .SingleAsync(
                    target => target.WebAppId == appId && target.Id == targetId,
                    cancellationToken);
        }
    }

    private sealed class FakeHubContext(
        Func<string, string, AppMediaFileMetadataResponse> onGetMetadata,
        Action<FetchFileChunkRequest> onFetch) : IHubContext<AutomatonHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients(onGetMetadata, onFetch);
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class FakeHubClients(
        Func<string, string, AppMediaFileMetadataResponse> onGetMetadata,
        Action<FetchFileChunkRequest> onFetch) : IHubClients
    {
        private readonly FakeClientProxy clientProxy = new(onGetMetadata, onFetch);

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public ISingleClientProxy Client(string connectionId) => clientProxy;
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => clientProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeClientProxy(
        Func<string, string, AppMediaFileMetadataResponse> onGetMetadata,
        Action<FetchFileChunkRequest> onFetch) : ISingleClientProxy
    {
        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == AutomatonHubMethods.GetAppMediaFileMetadata &&
                args.Length >= 2 &&
                args[0] is string appId &&
                args[1] is string relativePath)
            {
                return Task.FromResult((T)(object)onGetMetadata(appId, relativePath));
            }

            throw new NotSupportedException($"Unexpected invoke method: {method}");
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (method == AutomatonHubMethods.FetchFileChunk &&
                args.Length > 0 &&
                args[0] is FetchFileChunkRequest request)
            {
                onFetch(request);
            }

            return Task.CompletedTask;
        }
    }
}
