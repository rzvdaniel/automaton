using System.Security.Claims;
using System.Text.Json;
using Automaton.Api.Controllers;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ServerOsUpdateTests
{
    private sealed class Audit : IAuditEventService
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) { Events.Add(request); return Task.CompletedTask; }
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Host : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "osupdate-" + Guid.NewGuid().ToString("N"))).FullName;
        public string Requests => Directory.CreateDirectory(Path.Combine(Root, "requests")).FullName;
        public string Status => Directory.CreateDirectory(Path.Combine(Root, "status")).FullName;
        public string Backups => Directory.CreateDirectory(Path.Combine(Root, "backups")).FullName;
        public string RestartRequests => Directory.CreateDirectory(Path.Combine(Root, "restart-requests")).FullName;
        public IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Installation:OsUpdateRequestsDirectory"] = Path.Combine(Root, "requests"),
            ["Installation:OsUpdateStatusDirectory"] = Path.Combine(Root, "status"),
            ["Installation:RestartRequestsDirectory"] = Path.Combine(Root, "restart-requests"),
            ["Installation:RestartStatusDirectory"] = Path.Combine(Root, "restart-status"),
            ["Installation:UpdateRequestsDirectory"] = Path.Combine(Root, "updates"),
            ["Installation:UpdateStatusDirectory"] = Path.Combine(Root, "update-status"),
            ["Installation:BackupRequestsDirectory"] = Backups,
            ["Installation:StatusDirectory"] = Path.Combine(Root, "none"),
            ["Installation:BootTimeFile"] = Path.Combine(Root, "stat")
        }).Build();
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AppRun_NeedsAnAdministratorViewerToken_AuditsFirst_AndWritesOneRequest()
    {
        using var host = new Host();
        var appId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var tokens = new ViewerTokenService(DataProtectionProvider.Create("osupdate" + Guid.NewGuid().ToString("N")));
        var db = AppAuthorizationTestData.CreateDbContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = roleId, Name = UserRole.SystemAdministrator, NormalizedName = UserRole.SystemAdministrator.ToUpperInvariant() });
        db.UserRoles.Add(new ApplicationUserRole { UserId = admin, RoleId = roleId });
        await db.SaveChangesAsync();
        var authorization = new ResourceAuthorizationService(db, new UserContextService(new HttpContextAccessor()));
        var audit = new Audit();
        _ = host.Requests;

        async Task<int> Post(string? token, string? scope = null)
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", appId.ToString())], "AppPermission")) };
            if (token is not null) http.Request.Headers[ViewerHeaders.Token] = token;
            var controller = new ServerOsUpdateRunController(db, host.Configuration, audit, tokens, authorization) { ControllerContext = new ControllerContext { HttpContext = http } };
            return (await controller.Run(new StartServerOsUpdateRequest(scope), default) as IStatusCodeActionResult)?.StatusCode ?? 0;
        }

        Assert.That(await Post(null), Is.EqualTo(403));
        Assert.That(await Post("forged"), Is.EqualTo(403));
        Assert.That(await Post(tokens.Create(appId, Guid.NewGuid())), Is.EqualTo(403), "a viewer who is not an administrator");
        Assert.That(await Post(tokens.Create(Guid.NewGuid(), admin)), Is.EqualTo(403), "a token for another app");
        Assert.That(audit.Events, Is.Empty, "refused calls leave no request and no audit event");
        Assert.That(Directory.GetFiles(host.Requests), Is.Empty);

        Assert.That(await Post(tokens.Create(appId, admin), "dist-upgrade"), Is.EqualTo(400));
        Assert.That(Directory.GetFiles(host.Requests), Is.Empty);
        Assert.That(await Post(tokens.Create(appId, admin), "all"), Is.EqualTo(202));
        var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(host.Requests, "request.json"))).RootElement;
        Assert.That(request.GetProperty("scope").GetString(), Is.EqualTo("all"));
        var logged = audit.Events.Last();
        Assert.That(logged.EventType, Is.EqualTo("ServerOsUpdateRequested"));
        Assert.That(logged.ActorUserId, Is.EqualTo(admin));
        Assert.That(logged.FailOnError, Is.True);
        Assert.That(await Post(tokens.Create(appId, admin)), Is.EqualTo(409), "a second request while one is waiting");
    }

    [Test]
    public async Task Request_DefaultsToSecurity_IsRefusedWhileBusy_AndNeedsTheHostDirectory()
    {
        using var host = new Host();
        var db = AppAuthorizationTestData.CreateDbContext();
        var missing = await ServerOsUpdate.RequestAsync(db, host.Configuration, "admin", null, Now, default);
        Assert.That(missing.Result, Is.EqualTo(ServerOsUpdateResult.TriggerMissing));

        _ = host.Requests;
        File.WriteAllText(Path.Combine(host.Backups, "request.json"), "{}");
        var backup = await ServerOsUpdate.RequestAsync(db, host.Configuration, "admin", null, Now, default);
        Assert.That(backup.Result, Is.EqualTo(ServerOsUpdateResult.Busy));
        Assert.That(backup.Reason, Does.Contain("backup"));
        File.Delete(Path.Combine(host.Backups, "request.json"));

        _ = host.RestartRequests;
        File.WriteAllText(Path.Combine(host.RestartRequests, "request.json"), "{}");
        Assert.That((await ServerOsUpdate.RequestAsync(db, host.Configuration, "admin", null, Now, default)).Result, Is.EqualTo(ServerOsUpdateResult.Busy), "a restart is waiting");
        File.Delete(Path.Combine(host.RestartRequests, "request.json"));
        Assert.That(Directory.GetFiles(host.Requests), Is.Empty, "a refused request writes nothing");

        Assert.That((await ServerOsUpdate.RequestAsync(db, host.Configuration, "admin", null, Now, default)).Result, Is.EqualTo(ServerOsUpdateResult.Accepted));
        Assert.That(JsonDocument.Parse(File.ReadAllText(Path.Combine(host.Requests, "request.json"))).RootElement.GetProperty("scope").GetString(), Is.EqualTo("security"));
    }

    [Test]
    public async Task ARunningOsUpdate_BlocksARestartAndASecondUpdate()
    {
        using var host = new Host();
        _ = host.Requests;
        var db = AppAuthorizationTestData.CreateDbContext();
        File.WriteAllText(Path.Combine(host.Status, "status.json"), JsonSerializer.Serialize(new { state = "running", startedAtUtc = Now.AddMinutes(-5) }));
        _ = host.RestartRequests;
        var restart = await ServerRestart.RequestAsync(db, host.Configuration, "admin", null, Now, default);
        Assert.That(restart.Result, Is.EqualTo(ServerRestartResult.Busy));
        Assert.That(restart.Reason, Does.Contain("OS update"));
        Assert.That((await ServerOsUpdate.RequestAsync(db, host.Configuration, "admin", null, Now, default)).Result, Is.EqualTo(ServerOsUpdateResult.InProgress));

        File.WriteAllText(Path.Combine(host.Status, "status.json"), JsonSerializer.Serialize(new { state = "running", startedAtUtc = Now.AddHours(-5) }));
        Assert.That((await ServerOsUpdate.ReadAsync(host.Configuration, Now, default)).State, Is.EqualTo("failed"), "a stuck run is not active forever");
    }

    [Test]
    public async Task Read_ReportsTheResultTheRestartNeededAndTheLog()
    {
        using var host = new Host();
        _ = host.Requests;
        File.WriteAllText(Path.Combine(host.Status, "status.json"), JsonSerializer.Serialize(new
        {
            state = "succeeded", scope = "security", requestedBy = "admin", requestedAtUtc = Now.AddMinutes(-3), finishedAtUtc = Now.AddMinutes(-1),
            upgradedCount = 4, restartRequired = true, message = "Installed 4 update(s).", logTail = "Setting up openssl"
        }));
        var status = await ServerOsUpdate.ReadAsync(host.Configuration, Now, default);
        Assert.That(status.TriggerAvailable, Is.True);
        Assert.That(status.State, Is.EqualTo("succeeded"));
        Assert.That(status.UpgradedCount, Is.EqualTo(4));
        Assert.That(status.RestartRequired, Is.True);
        Assert.That(status.LogTail, Is.EqualTo("Setting up openssl"));
        File.WriteAllText(Path.Combine(host.Requests, "request.json"), "{}");
        Assert.That((await ServerOsUpdate.ReadAsync(host.Configuration, Now, default)).State, Is.EqualTo("requested"));
    }

    [Test]
    public void ServerUpdateOs_IsASupportedPermission_WithItsOwnAppOnlyPolicy_AndAdminOnlyPost()
    {
        Assert.That(Automaton.Core.Models.ApplicationContractValidator.SupportedPermissions, Does.Contain("server:update-os"));
        Assert.That(typeof(ServerOsUpdateRunController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single().Policy, Is.EqualTo("AppPermission:server:update-os"));
        var post = typeof(InstallOsUpdatesController).GetMethod(nameof(InstallOsUpdatesController.Post))!;
        var authorize = post.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.That(authorize.Roles, Is.EqualTo(UserRole.SystemAdministrator));
    }
}
