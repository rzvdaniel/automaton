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

    [TestCase(false, "Failed", "boom", true, true)]
    [TestCase(false, "Failed", null, true, false)] // crashed after a good deploy: not a failed deploy
    [TestCase(false, "Failed", "boom", false, false)]
    [TestCase(false, "Running", null, true, false)]
    [TestCase(true, "Failed", "boom", true, false)]
    public void PreviousVersionServing_NeedsAFailedDeployAndAnEarlierSuccess(bool paused, string status, string? error, bool hadSuccess, bool expected)
        => Assert.That(AppMcpReader.PreviousVersionServing(paused, status, error, hadSuccess ? DateTime.UtcNow : null), Is.EqualTo(expected));

    private sealed class FakeRunner(Guid expectedRunner) : Automaton.Api.ServerCleanup.IRunnerMaintenanceCommands
    {
        public List<Automaton.Core.Models.AppActionCommand> Sent { get; } = [];
        public Task<System.Text.Json.JsonElement> SendAsync(Guid runnerId, Guid actor, Automaton.Core.Models.AppActionCommand command, TimeSpan timeout, CancellationToken token)
        {
            Assert.That(runnerId, Is.EqualTo(expectedRunner));
            Sent.Add(command);
            return Task.FromResult(System.Text.Json.JsonSerializer.SerializeToElement(
                new Automaton.Core.Models.ReadAppLogsResult(Guid.Empty, Guid.Empty, true, "beat 1\nbeat 2\n")));
        }
    }

    [Test]
    public async Task Logs_OfALongRunningApp_ReturnItsOwnRecentOutput()
    {
        var ownerId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        using var tempRoot = new AppAuthorizationTestData.TemporaryDirectory();
        await using var db = AppAuthorizationTestData.CreateDbContext();
        AppAuthorizationTestData.SeedApp(db, appId, ownerId, "heartbeat");
        db.Runners.Add(new() { Id = runnerId, Name = "runner", RunnerUsers = [] });
        db.AppDeploymentTargets.Add(new() { Id = Guid.NewGuid(), WebAppId = appId, Name = "live", Slug = "live", ExecutionRunnerId = runnerId, GenericRuntimeDeployed = true });
        await db.SaveChangesAsync();
        var apps = AppAuthorizationTestServiceFactory.CreateAppsService(db, ownerId, tempRoot.Path, out var storage);
        var users = AppAuthorizationTestData.CreateUserContextService(ownerId);
        var runner = new FakeRunner(runnerId);
        var reader = new AppMcpReader(apps, AppAuthorizationActionExecutionTestFactory.Create(db, ownerId), new NoEvents(),
            db, new ResourceAuthorizationService(db, users), users, storage, null, runner);

        var json = System.Text.Json.JsonSerializer.Serialize(await reader.LogsAsync(appId, null, 50, default));

        Assert.That(json, Does.Contain("beat 1").And.Contain("beat 2"));
        Assert.That(runner.Sent.Select(c => c.Kind), Is.EqualTo(new[] { "app-logs" }));
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
