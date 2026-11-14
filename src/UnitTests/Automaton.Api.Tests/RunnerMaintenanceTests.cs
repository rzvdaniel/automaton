using System.Security.Claims;
using Automaton.Api.Authentication;
using Automaton.Api.Controllers;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerMaintenanceTests
{
    private sealed class Audit : IAuditEventService
    {
        public List<(string Action, string Message, string Metadata)> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null, string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default)
        {
            Events.Add((action, message, System.Text.Json.JsonSerializer.Serialize(metadata)));
            return Task.CompletedTask;
        }
    }

    private sealed class Setup
    {
        public required ApplicationDbContext Db { get; init; }
        public required ViewerTokenService Tokens { get; init; }
        public required RunnerMaintenanceService Maintenance { get; init; }
        public required ServerCleanupService Cleanup { get; init; }
        public required ResourceAuthorizationService Authorization { get; init; }
        public required Audit Audit { get; init; }
        public Guid AppId { get; init; } = Guid.NewGuid();
        public Guid Admin { get; init; } = Guid.NewGuid();
        public Guid Other { get; init; } = Guid.NewGuid();
        public Guid RunnerOne { get; init; } = Guid.NewGuid();
        public Guid RunnerTwo { get; init; } = Guid.NewGuid();

        public async Task<int> Post(string? viewerToken, Guid runner, bool app = true)
        {
            var http = new DefaultHttpContext();
            if (app) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", AppId.ToString())], "AppPermission"));
            if (viewerToken is not null) http.Request.Headers[ViewerHeaders.Token] = viewerToken;
            var controller = new RunnerMaintenanceController(Maintenance, Cleanup, Tokens, Authorization) { ControllerContext = new ControllerContext { HttpContext = http } };
            return ((IStatusCodeActionResult)await controller.RunCleanup(runner, default)).StatusCode ?? 200;
        }
    }

    private static async Task<Setup> SetUp()
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var audit = new Audit();
        var cleanup = new ServerCleanupService(db, new FakeRunner());
        var setup = new Setup
        {
            Db = db, Tokens = new ViewerTokenService(DataProtectionProvider.Create("rm" + Guid.NewGuid().ToString("N"))),
            Audit = audit, Cleanup = cleanup, Maintenance = new RunnerMaintenanceService(db, cleanup, audit),
            Authorization = new ResourceAuthorizationService(db, new UserContextService(new HttpContextAccessor()))
        };
        var owner = Guid.NewGuid();
        await InternalRuntimeControllerTests.SeedRestrictedAppAsync(db, setup.AppId, owner);
        var role = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = role, Name = UserRole.SystemAdministrator, NormalizedName = UserRole.SystemAdministrator.ToUpperInvariant() });
        db.UserRoles.Add(new ApplicationUserRole { UserId = setup.Admin, RoleId = role });
        foreach (var (id, name) in new[] { (setup.RunnerOne, "runner-01"), (setup.RunnerTwo, "runner-02") })
            db.Runners.Add(new Runner
            {
                Id = id, Name = name, Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
                InstallationId = $"sh_{Guid.NewGuid():N}", ConnectionId = "c", LastHeartbeatAtUtc = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
        return setup;
    }

    [Test]
    public async Task CleanupNeedsTheAdminViewerTokenAndThatMachinesApproval()
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        var admin = s.Tokens.Create(s.AppId, s.Admin);

        Assert.That(await s.Post(admin, s.RunnerOne, app: false), Is.EqualTo(403), "not an app");
        Assert.That(await s.Post(null, s.RunnerOne), Is.EqualTo(403), "no viewer token");
        Assert.That(await s.Post("forged", s.RunnerOne), Is.EqualTo(403));
        Assert.That(await s.Post(s.Tokens.Create(s.AppId, s.Other), s.RunnerOne), Is.EqualTo(403), "a viewer who is not an administrator");
        Assert.That(await s.Post(s.Tokens.Create(Guid.NewGuid(), s.Admin), s.RunnerOne), Is.EqualTo(403), "a token for another app");
        Assert.That(await s.Post(admin, s.RunnerTwo), Is.EqualTo(403), "permission approved for runner-01 only");
        Assert.That(s.Db.ServerCleanupRuns, Is.Empty, "nothing queued by any refused call");
        Assert.That(await s.Post(admin, s.RunnerOne), Is.EqualTo(202));
        var run = s.Db.ServerCleanupRuns.Single();
        Assert.That((run.RunnerId, run.Result), Is.EqualTo((s.RunnerOne, "queued")));
    }

    [Test]
    public async Task AnApprovedCleanupIsAuditLoggedWithWhoAskedAndWhere()
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        s.Audit.Events.Clear();
        await s.Post(s.Tokens.Create(s.AppId, s.Admin), s.RunnerOne);
        var entry = s.Audit.Events.Single();
        Assert.That(entry.Action, Is.EqualTo("Requested"));
        Assert.That(entry.Message, Does.Contain("runner-01"));
        Assert.That(entry.Metadata, Does.Contain(s.Admin.ToString()).And.Contain("host-cleanup"));
    }

    [Test]
    public async Task ApprovalAndRevocationPerMachineAreAuditLogged()
    {
        var s = await SetUp();
        var list = await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        Assert.That(list.Single(x => x.RunnerId == s.RunnerOne).Approved, Is.True);
        Assert.That(list.Single(x => x.RunnerId == s.RunnerTwo).Approved, Is.False);
        list = await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, false, default);
        Assert.That(list.Any(x => x.Approved), Is.False);
        Assert.That(s.Audit.Events.Select(e => e.Action), Is.EqualTo(new[] { "Approved", "Revoked" }));
        Assert.ThrowsAsync<KeyNotFoundException>(() => s.Maintenance.SetAsync(s.AppId, Guid.NewGuid(), true, default));
        Assert.ThrowsAsync<KeyNotFoundException>(() => s.Maintenance.SetAsync(Guid.NewGuid(), s.RunnerOne, true, default));
    }

    [Test]
    public async Task OfflineMissingAndBusyMachinesAreRefusedAfterTheChecks()
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        var admin = s.Tokens.Create(s.AppId, s.Admin);
        Assert.That(await s.Post(admin, s.RunnerOne), Is.EqualTo(202));
        Assert.That(await s.Post(admin, s.RunnerOne), Is.EqualTo(409), "one cleanup at a time");
        s.Db.Runners.Single(x => x.Id == s.RunnerOne).ConnectionId = null;
        s.Db.ServerCleanupRuns.RemoveRange(s.Db.ServerCleanupRuns);
        await s.Db.SaveChangesAsync();
        Assert.That(await s.Post(admin, s.RunnerOne), Is.EqualTo(409), "offline");
    }

    [Test]
    public async Task ARunIsReadOnlyForAnAppApprovedForThatMachine()
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        await s.Post(s.Tokens.Create(s.AppId, s.Admin), s.RunnerOne);
        var run = s.Db.ServerCleanupRuns.Single();
        async Task<int> Get(Guid runner, Guid id)
        {
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", s.AppId.ToString())], "AppPermission")) };
            var controller = new RunnerMaintenanceController(s.Maintenance, s.Cleanup, s.Tokens, s.Authorization) { ControllerContext = new ControllerContext { HttpContext = http } };
            return ((IStatusCodeActionResult)await controller.GetRun(runner, id, default)).StatusCode ?? 200;
        }
        Assert.That(await Get(s.RunnerOne, run.Id), Is.EqualTo(200));
        Assert.That(await Get(s.RunnerOne, Guid.NewGuid()), Is.EqualTo(404));
        Assert.That(await Get(s.RunnerTwo, run.Id), Is.EqualTo(403));
    }

    [Test]
    public void OnlyAnAppWithTheApprovedPermissionReachesTheEndpoints()
    {
        var policy = typeof(RunnerMaintenanceController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo(AppPermissionAuthenticationOptions.RunnerMaintainPolicy));
        Assert.That(typeof(AdminAppRunnerPermissionsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles,
            Is.EqualTo(UserRole.SystemAdministrator));
        Assert.That(Automaton.Core.Models.ApplicationContractValidator.SupportedPermissions, Does.Contain(AppPermissionApproval.RunnerMaintain));
    }
}
