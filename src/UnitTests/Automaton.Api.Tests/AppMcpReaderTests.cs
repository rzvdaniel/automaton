using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Automaton.Api.Data;
using Automaton.Api.Extensions;
using Automaton.Api.Mcp;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppMcpReaderTests
{
    private sealed class NoEvents : IApplicationEventService
    {
        public Task WriteAsync(ApplicationEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PagedResult<ApplicationEventDto>> GetEventsAsync(Guid appId, ApplicationEventQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<ApplicationEventDto>());
    }

    private static (AppMcpReader Reader, ApplicationDbContext Db) Create(ApplicationDbContext db, Guid userId, string root)
    {
        var apps = AppAuthorizationTestServiceFactory.CreateAppsService(db, userId, root, out var storage);
        var users = AppAuthorizationTestData.CreateUserContextService(userId);
        var reader = new AppMcpReader(apps, AppAuthorizationActionExecutionTestFactory.Create(db, userId), new NoEvents(),
            db, new ResourceAuthorizationService(db, users), users, storage);
        return (reader, db);
    }

    [Test]
    public async Task EveryTool_ShowsOnlyTheCallersApps()
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        var root = tempRoot.Path;
        await using var db = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "owner-app");
        db.WebApps.Single().Summary = "Card summary";
        db.AppModuleExecutions.Add(new() { Id = Guid.NewGuid(), AppId = appId, RequestedByUserId = ownerId, ModuleKey = "check", Status = "Failed", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var owner = Create(db, ownerId, root).Reader;

        Assert.That(System.Text.Json.JsonSerializer.Serialize(await owner.ListAppsAsync(default)), Does.Contain(appId.ToString()));
        Assert.That(System.Text.Json.JsonSerializer.Serialize(await owner.ActivityAsync(appId, default)), Does.Contain("check"));
        var stranger = Create(db, strangerId, root).Reader;
        Assert.That(System.Text.Json.JsonSerializer.Serialize(await stranger.ListAppsAsync(default)), Does.Not.Contain(appId.ToString()));
        Assert.ThrowsAsync<KeyNotFoundException>(() => stranger.StatusAsync(appId, default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => stranger.ActivityAsync(appId, default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => stranger.LogsAsync(appId, null, null, default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => stranger.DocsAsync(appId, default));
    }

    [Test]
    public async Task Status_ForUnknownApp_IsNotFound()
    {
        await using var db = AppAuthorizationTestData.CreateDbContext();
        var reader = Create(db, Guid.NewGuid(), Path.GetTempPath()).Reader;
        Assert.ThrowsAsync<KeyNotFoundException>(() => reader.StatusAsync(Guid.NewGuid(), default));
    }

    private sealed class NoUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }

    [Test]
    public async Task McpEndpoint_WithoutAKey_Answers401()
    {
        using var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s =>
            {
                s.AddRouting();
                s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, NoUserHandler>("Test", _ => { }).AddCookie("Identity.Application");
                s.AddApiCookieAuthentication();
                s.AddAuthorization(o => o.AddPolicy("DeveloperApiAccess", p => p.AddAuthenticationSchemes("Test", "Identity.Application").RequireAuthenticatedUser()));
                s.AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<AppMcpTools>();
                s.AddScoped(_ => (AppMcpReader)null!);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e => e.MapMcp("/mcp").RequireAuthorization("DeveloperApiAccess"));
            });
        }).StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        using var response = await host.GetTestClient().SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
