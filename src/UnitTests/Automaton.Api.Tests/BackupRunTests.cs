using System.Security.Claims;
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
public sealed class BackupRunTests
{
    private sealed class Audit : IAuditEventService
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) { Events.Add(request); return Task.CompletedTask; }
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Test]
    public async Task Run_NeedsAnAdministratorViewerToken_AuditsFirst_AndDropsOneRequest()
    {
        var appId = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var other = Guid.NewGuid();
        var tokens = new ViewerTokenService(DataProtectionProvider.Create("backup" + Guid.NewGuid().ToString("N")));
        var db = AppAuthorizationTestData.CreateDbContext();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = roleId, Name = UserRole.SystemAdministrator, NormalizedName = UserRole.SystemAdministrator.ToUpperInvariant() });
        db.UserRoles.Add(new ApplicationUserRole { UserId = admin, RoleId = roleId });
        await db.SaveChangesAsync();
        var authorization = new ResourceAuthorizationService(db, new UserContextService(new HttpContextAccessor()));
        using var directory = new TempDirectory();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Installation:BackupRequestsDirectory"] = directory.Path }).Build();
        var audit = new Audit();

        async Task<int> Post(string? token)
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", appId.ToString())], "AppPermission")) };
            if (token is not null) http.Request.Headers[ViewerHeaders.Token] = token;
            var controller = new BackupRunController(configuration, tokens, authorization, audit) { ControllerContext = new ControllerContext { HttpContext = http } };
            return (await controller.Run(default) as IStatusCodeActionResult)?.StatusCode ?? 0;
        }

        Assert.That(await Post(null), Is.EqualTo(403));
        Assert.That(await Post("forged"), Is.EqualTo(403));
        Assert.That(await Post(tokens.Create(appId, other)), Is.EqualTo(403));
        Assert.That(await Post(tokens.Create(Guid.NewGuid(), admin)), Is.EqualTo(403));
        Assert.That(await Post(tokens.Create(appId, admin, TimeSpan.FromSeconds(-1))), Is.EqualTo(403));
        Assert.That(audit.Events, Is.Empty, "refused calls leave no request and no audit event");
        Assert.That(Directory.GetFiles(directory.Path), Is.Empty);

        Assert.That(await Post(tokens.Create(appId, admin)), Is.EqualTo(202));
        Assert.That(File.Exists(Path.Combine(directory.Path, "request.json")), Is.True);
        var logged = audit.Events.Single();
        Assert.That(logged.ActorUserId, Is.EqualTo(admin));
        Assert.That(logged.FailOnError, Is.True);
        Assert.That(await Post(tokens.Create(appId, admin)), Is.EqualTo(409), "a second request while one is waiting");
    }

    [Test]
    public void BackupsRun_IsASupportedPermission_AndHasItsOwnAppOnlyPolicy()
    {
        Assert.That(Automaton.Core.Models.ApplicationContractValidator.SupportedPermissions, Does.Contain("backups:run"));
        Assert.That(typeof(BackupRunController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single().Policy, Is.EqualTo("AppPermission:backups:run"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "backup-run-" + Guid.NewGuid().ToString("N"))).FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
