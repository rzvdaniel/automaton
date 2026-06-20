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

        await using var dbContext = CreateDbContext();
        dbContext.WebApps.Add(new WebApp
        {
            Id = appId,
            Name = "Media App",
            Summary = string.Empty,
            Description = string.Empty,
            Definition = "{}",
            ExecutionRunnerId = runnerId,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        dbContext.Runners.Add(new Runner
        {
            Id = runnerId,
            Name = "runner-1",
            ConnectionId = "runner-connection",
            LastHeartbeatAtUtc = DateTime.UtcNow,
            RunnerUsers = new List<RunnerUser>
            {
                new() { RunnerId = runnerId, UserId = ownerId }
            }
        });
        await dbContext.SaveChangesAsync();

        var downloadRegistry = new FileDownloadRegistry();
        var hubContext = new FakeHubContext(request =>
        {
            downloadRegistry.TryWrite(new FileChunkReadyMessage
            {
                SessionId = request.SessionId,
                StatusCode = StatusCodes.Status200OK,
                ContentType = "image/png",
                FileName = "photo.png",
                Data = [1, 2, 3],
                IsLast = true
            });
        });

        var service = new AppMediaStorageService(
            dbContext,
            hubContext,
            new FileUploadRegistry(),
            downloadRegistry,
            Options.Create(new MediaStorageOptions()),
            NullLogger<AppMediaStorageService>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var statusCode = await service.ProxyReadAsync(appId, ownerId, "gallery/photo.png", httpContext.Response, CancellationToken.None);

        Assert.That(statusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(httpContext.Response.ContentType, Is.EqualTo("image/png"));
        Assert.That(httpContext.Response.Headers["X-Content-Type-Options"].ToString(), Is.EqualTo("nosniff"));
        Assert.That(httpContext.Response.Headers["Cache-Control"].ToString(), Is.EqualTo("public, max-age=3600"));
        Assert.That(httpContext.Response.Headers["Content-Disposition"].ToString(), Is.EqualTo("inline; filename=\"photo.png\""));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FakeHubContext(Action<FetchFileChunkRequest> onFetch) : IHubContext<AutomatonHub>
    {
        public IHubClients Clients { get; } = new FakeHubClients(onFetch);
        public IGroupManager Groups { get; } = null!;
    }

    private sealed class FakeHubClients(Action<FetchFileChunkRequest> onFetch) : IHubClients
    {
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => new FakeClientProxy(onFetch);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeClientProxy(Action<FetchFileChunkRequest> onFetch) : IClientProxy
    {
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
