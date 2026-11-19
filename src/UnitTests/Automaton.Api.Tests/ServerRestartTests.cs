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
public sealed class ServerRestartTests
{
    private sealed class Audit : IAuditEventService
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) { Events.Add(request); return Task.CompletedTask; }
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Host : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "restart-" + Guid.NewGuid().ToString("N"))).FullName;
        public string Requests => Directory.CreateDirectory(Path.Combine(Root, "requests")).FullName;
        public string Status => Directory.CreateDirectory(Path.Combine(Root, "status")).FullName;
        public string Updates => Directory.CreateDirectory(Path.Combine(Root, "updates")).FullName;
        public string UpdateStatus => Directory.CreateDirectory(Path.Combine(Root, "update-status")).FullName;
        public string Backups => Directory.CreateDirectory(Path.Combine(Root, "backups")).FullName;
        public string Stat => Path.Combine(Root, "stat");
        public IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Installation:RestartRequestsDirectory"] = Path.Combine(Root, "requests"),
            ["Installation:RestartStatusDirectory"] = Path.Combine(Root, "status"),
            ["Installation:UpdateRequestsDirectory"] = Updates,
            ["Installation:UpdateStatusDirectory"] = UpdateStatus,
            ["Installation:BackupRequestsDirectory"] = Backups,
            ["Installation:StatusDirectory"] = Path.Combine(Root, "none"),
            ["Installation:BootTimeFile"] = Stat
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
        var tokens = new ViewerTokenService(DataProtectionProvider.Create("restart" + Guid.NewGuid().ToString("N")));
        var db = AppAuthorizationTestData.CreateDbContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = roleId, Name = UserRole.SystemAdministrator, NormalizedName = UserRole.SystemAdministrator.ToUpperInvariant() });
        db.UserRoles.Add(new ApplicationUserRole { UserId = admin, RoleId = roleId });
        await db.SaveChangesAsync();
        var authorization = new ResourceAuthorizationService(db, new UserContextService(new HttpContextAccessor()));
        var audit = new Audit();
        _ = host.Requests;

        async Task<int> Post(string? token, DateTimeOffset? at = null)
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", appId.ToString())], "AppPermission")) };
            if (token is not null) http.Request.Headers[ViewerHeaders.Token] = token;
            var controller = new ServerRestartRunController(db, host.Configuration, audit, tokens, authorization) { ControllerContext = new ControllerContext { HttpContext = http } };
            return (await controller.Run(new StartServerRestartRequest(at), default) as IStatusCodeActionResult)?.StatusCode ?? 0;
        }

        Assert.That(await Post(null), Is.EqualTo(403));
        Assert.That(await Post("forged"), Is.EqualTo(403));
        Assert.That(await Post(tokens.Create(appId, Guid.NewGuid())), Is.EqualTo(403), "a viewer who is not an administrator");
        Assert.That(await Post(tokens.Create(Guid.NewGuid(), admin)), Is.EqualTo(403), "a token for another app");
        Assert.That(audit.Events, Is.Empty, "refused calls leave no request and no audit event");
        Assert.That(Directory.GetFiles(host.Requests), Is.Empty);

        Assert.That(await Post(tokens.Create(appId, admin), DateTimeOffset.UtcNow.AddDays(8)), Is.EqualTo(400), "too far away");
        File.Delete(Path.Combine(host.Requests, "request.json"));
        Assert.That(await Post(tokens.Create(appId, admin), DateTimeOffset.UtcNow.AddHours(3)), Is.EqualTo(202));
        var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(host.Requests, "request.json"))).RootElement;
        Assert.That(request.GetProperty("restartAtUtc").GetString(), Does.EndWith("Z"));
        var logged = audit.Events.Last();
        Assert.That(logged.EventType, Is.EqualTo("ServerRestartRequested"));
        Assert.That(logged.ActorUserId, Is.EqualTo(admin));
        Assert.That(logged.FailOnError, Is.True);
        Assert.That(await Post(tokens.Create(appId, admin)), Is.EqualTo(409), "a second request while one is waiting");
    }

    [Test]
    public async Task Request_IsRefusedWhileABackupUpdateDeployOrRunnerUpgradeRuns()
    {
        using var host = new Host();
        _ = host.Requests;
        var db = AppAuthorizationTestData.CreateDbContext();
        var config = host.Configuration;
        async Task<(ServerRestartResult, string?)> Ask() => await ServerRestart.RequestAsync(db, config, "admin", null, Now, default);

        File.WriteAllText(Path.Combine(host.Backups, "request.json"), "{}");
        var backup = await Ask();
        Assert.That(backup.Item1, Is.EqualTo(ServerRestartResult.Busy));
        Assert.That(backup.Item2, Does.Contain("backup"));
        File.Delete(Path.Combine(host.Backups, "request.json"));

        Directory.CreateDirectory(Path.Combine(host.Root, "none"));
        File.WriteAllText(Path.Combine(host.Root, "none", "backup.running"), "");
        var running = await Ask();
        Assert.That(running.Item1, Is.EqualTo(ServerRestartResult.Busy), "a backup that already started");
        Assert.That(running.Item2, Does.Contain("backup"));
        File.Delete(Path.Combine(host.Root, "none", "backup.running"));

        File.WriteAllText(Path.Combine(host.UpdateStatus, "status.json"), "{\"state\":\"running\",\"startedAtUtc\":\"" + Now.AddMinutes(-5).ToString("o") + "\"}");
        var update = await Ask();
        Assert.That(update.Item1, Is.EqualTo(ServerRestartResult.Busy));
        Assert.That(update.Item2, Does.Contain("update"));
        File.WriteAllText(Path.Combine(host.UpdateStatus, "status.json"), "{\"state\":\"succeeded\"}");

        db.DeploymentOperations.Add(new DeploymentOperation { Id = Guid.NewGuid(), WebAppId = Guid.NewGuid(), PackagePath = "x.zip", Status = DeploymentOperationStatuses.Running, CreatedAtUtc = Now.UtcDateTime });
        await db.SaveChangesAsync();
        var deploy = await Ask();
        Assert.That(deploy.Item1, Is.EqualTo(ServerRestartResult.Busy));
        Assert.That(deploy.Item2, Does.Contain("deploy"));
        db.DeploymentOperations.RemoveRange(db.DeploymentOperations);
        db.InfrastructureRunnerUpdateOperations.Add(new InfrastructureRunnerUpdateOperation { Id = Guid.NewGuid(), Status = "replacingRunner", RequestedAtUtc = Now.UtcDateTime });
        await db.SaveChangesAsync();
        var runner = await Ask();
        Assert.That(runner.Item1, Is.EqualTo(ServerRestartResult.Busy));
        Assert.That(runner.Item2, Does.Contain("Runner"));
        Assert.That(Directory.GetFiles(host.Requests), Is.Empty, "a refused request writes nothing");

        db.InfrastructureRunnerUpdateOperations.RemoveRange(db.InfrastructureRunnerUpdateOperations);
        await db.SaveChangesAsync();
        Assert.That((await Ask()).Item1, Is.EqualTo(ServerRestartResult.Accepted));
    }

    [Test]
    public async Task Request_WithoutTheHostDirectory_ReportsTheTriggerMissing()
    {
        using var host = new Host();
        var db = AppAuthorizationTestData.CreateDbContext();
        var result = await ServerRestart.RequestAsync(db, host.Configuration, "admin", null, Now, default);
        Assert.That(result.Result, Is.EqualTo(ServerRestartResult.TriggerMissing));
    }

    [Test]
    public async Task Read_ReportsScheduledRestartsAndWhenTheServerLastRestarted()
    {
        using var host = new Host();
        _ = host.Requests;
        File.WriteAllText(host.Stat, "cpu 1 2 3\nbtime " + Now.AddDays(-5).ToUnixTimeSeconds() + "\nprocs_running 1\n");
        File.WriteAllText(Path.Combine(host.Status, "status.json"), JsonSerializer.Serialize(new
        {
            state = "scheduled", requestedBy = "admin", requestedAtUtc = Now.AddHours(-1), restartAtUtc = Now.AddHours(3)
        }));
        var scheduled = await ServerRestart.ReadAsync(host.Configuration, Now, default);
        Assert.That(scheduled.State, Is.EqualTo("scheduled"));
        Assert.That(scheduled.RestartAtUtc, Is.EqualTo(Now.AddHours(3)));
        Assert.That(scheduled.LastRestartedAtUtc, Is.EqualTo(Now.AddDays(-5)));

        // The server booted after the request was made: it has already happened.
        File.WriteAllText(host.Stat, "btime " + Now.AddMinutes(-10).ToUnixTimeSeconds() + "\n");
        var done = await ServerRestart.ReadAsync(host.Configuration, Now, default);
        Assert.That(done.State, Is.EqualTo("idle"));
        Assert.That(done.LastRestartedAtUtc, Is.EqualTo(Now.AddMinutes(-10)));

        File.WriteAllText(Path.Combine(host.Requests, "request.json"), "{}");
        Assert.That((await ServerRestart.ReadAsync(host.Configuration, Now, default)).State, Is.EqualTo("requested"));
    }

    [Test]
    public void ServerRestart_IsASupportedPermission_WithItsOwnAppOnlyPolicy_AndAdminOnlyPost()
    {
        Assert.That(Automaton.Core.Models.ApplicationContractValidator.SupportedPermissions, Does.Contain("server:restart"));
        Assert.That(typeof(ServerRestartRunController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single().Policy, Is.EqualTo("AppPermission:server:restart"));
        var post = typeof(InstallRestartController).GetMethod(nameof(InstallRestartController.Post))!;
        var authorize = post.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.That(authorize.Roles, Is.EqualTo(UserRole.SystemAdministrator));
    }
}
