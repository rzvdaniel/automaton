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

    private sealed class Commands : IRunnerMaintenanceCommands
    {
        public List<(Guid Runner, Guid Actor, string Kind)> Sent { get; } = [];
        public Task<System.Text.Json.JsonElement> SendAsync(Guid runnerId, Guid actor, AppActionCommand command, TimeSpan timeout, CancellationToken token)
        {
            Sent.Add((runnerId, actor, command.Kind));
            object answer = command.Kind switch
            {
                "app-logs" => new ReadAppLogsResult(Guid.Empty, Guid.Empty, true, "line one\nline two"),
                "disk-report" => new HostDiskReport(1, 2, 3, 4, 5),
                "os-update" => new HostOsUpdateResult(true, "done", false),
                "reboot" => new HostRebootResult(true),
                _ => new RestartAppRuntimeResult(Guid.Empty, Guid.Empty, true)
            };
            return Task.FromResult(System.Text.Json.JsonSerializer.SerializeToElement(answer));
        }
    }

    private sealed class Setup
    {
        public required Commands Commands { get; init; }
        public Guid TargetOne { get; init; } = Guid.NewGuid();
        public Guid TargetTwo { get; init; } = Guid.NewGuid();

        public async Task<int> Call(string? viewerToken, Guid runner, Func<RunnerMaintenanceController, Task<IActionResult>> action, bool app = true)
        {
            var http = new DefaultHttpContext();
            if (app) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("app_id", AppId.ToString())], "AppPermission"));
            if (viewerToken is not null) http.Request.Headers[ViewerHeaders.Token] = viewerToken;
            var controller = new RunnerMaintenanceController(Maintenance, Cleanup, Tokens, Authorization) { ControllerContext = new ControllerContext { HttpContext = http } };
            return ((IStatusCodeActionResult)await action(controller)).StatusCode ?? 200;
        }

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
        var commands = new Commands();
        var cleanup = new ServerCleanupService(db, new FakeRunner());
        var setup = new Setup
        {
            Db = db, Tokens = new ViewerTokenService(DataProtectionProvider.Create("rm" + Guid.NewGuid().ToString("N"))),
            Audit = audit, Cleanup = cleanup, Commands = commands, Maintenance = new RunnerMaintenanceService(db, cleanup, audit, commands),
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
        foreach (var (target, runner) in new[] { (setup.TargetOne, setup.RunnerOne), (setup.TargetTwo, setup.RunnerTwo) })
            db.AppDeploymentTargets.Add(new AppDeploymentTarget { Id = target, WebAppId = setup.AppId, Name = "t" + target.ToString("N")[..6], Slug = "t" + target.ToString("N")[..6], ExecutionRunnerId = runner, GenericRuntimeDeployed = true });
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

    private static readonly (string Name, Func<Setup, Guid, Func<RunnerMaintenanceController, Task<IActionResult>>> Action)[] Operations =
    [
        ("logs", (s, r) => c => c.Logs(r, s.TargetOne, 50, default)),
        ("restart", (s, r) => c => c.Restart(r, s.TargetOne, default)),
        ("disk", (s, r) => c => c.DiskReport(r, default))
    ];

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task EveryOperationNeedsTheAdminViewerTokenAndThatMachinesApproval(int index)
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        var action = Operations[index].Action;
        var admin = s.Tokens.Create(s.AppId, s.Admin);

        Assert.That(await s.Call(admin, s.RunnerOne, action(s, s.RunnerOne), app: false), Is.EqualTo(403), "not an app");
        Assert.That(await s.Call(null, s.RunnerOne, action(s, s.RunnerOne)), Is.EqualTo(403), "no viewer token");
        Assert.That(await s.Call(s.Tokens.Create(s.AppId, s.Other), s.RunnerOne, action(s, s.RunnerOne)), Is.EqualTo(403), "not an administrator");
        Assert.That(await s.Call(admin, s.RunnerTwo, action(s, s.RunnerTwo)), Is.EqualTo(403), "approved for runner-01 only");
        Assert.That(s.Commands.Sent, Is.Empty, "no refused call reached a Runner");
        Assert.That(await s.Call(admin, s.RunnerOne, action(s, s.RunnerOne)), Is.EqualTo(200));
        Assert.That(s.Commands.Sent.Single().Actor, Is.EqualTo(s.Admin));
    }

    [Test]
    public async Task LogsRestartAndDiskSendOnlyNamedCommandsAndAreAuditLogged()
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        s.Audit.Events.Clear();
        var admin = s.Tokens.Create(s.AppId, s.Admin);
        foreach (var operation in Operations) await s.Call(admin, s.RunnerOne, operation.Action(s, s.RunnerOne));

        Assert.That(s.Commands.Sent.Select(x => x.Kind), Is.EqualTo(new[] { "app-logs", "restart-runtime", "disk-report" }));
        Assert.That(s.Audit.Events, Has.Count.EqualTo(3));
        Assert.That(s.Audit.Events.Select(e => e.Metadata).ToArray(), Has.All.Contain(s.Admin.ToString()).And.All.Contain(s.RunnerOne.ToString()));
        Assert.That(s.Audit.Events.Select(e => e.Metadata).ToArray(),
            Has.Some.Contain("app-logs").And.Some.Contain("app-restart").And.Some.Contain("disk-report"));
    }

    [Test]
    public async Task AnAppOnAnotherMachineOrAnUnreasonableLineCountIsRefused()
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        var admin = s.Tokens.Create(s.AppId, s.Admin);
        Assert.That(await s.Call(admin, s.RunnerOne, c => c.Logs(s.RunnerOne, s.TargetTwo, 50, default)), Is.EqualTo(404), "target of runner-02");
        Assert.That(await s.Call(admin, s.RunnerOne, c => c.Restart(s.RunnerOne, Guid.NewGuid(), default)), Is.EqualTo(404));
        Assert.That(await s.Call(admin, s.RunnerOne, c => c.Logs(s.RunnerOne, s.TargetOne, 0, default)), Is.EqualTo(400));
        Assert.That(await s.Call(admin, s.RunnerOne, c => c.Logs(s.RunnerOne, s.TargetOne, 5000, default)), Is.EqualTo(400));
        Assert.That(s.Commands.Sent, Is.Empty);
        var apps = await s.Maintenance.ListAppsAsync(s.RunnerOne, default);
        Assert.That(apps.Select(x => x.TargetId), Is.EqualTo(new[] { s.TargetOne }));
    }

    private static Func<RunnerMaintenanceController, Task<IActionResult>> Machine(string op, Guid runner, string? confirm)
        => c => op == "reboot" ? c.Reboot(runner, new(confirm), default) : c.OsUpdate(runner, new(confirm), default);

    [TestCase("os-update")]
    [TestCase("reboot")]
    public async Task OsUpdateAndRebootNeedTheAdminViewerThatMachinesApprovalAndItsTypedName(string op)
    {
        var s = await SetUp();
        await s.Maintenance.SetAsync(s.AppId, s.RunnerOne, true, default);
        var admin = s.Tokens.Create(s.AppId, s.Admin);
        var name = (await s.Db.Runners.FindAsync(s.RunnerOne))!.Name;

        Assert.That(await s.Call(admin, s.RunnerOne, Machine(op, s.RunnerOne, name), app: false), Is.EqualTo(403), "not an app");
        Assert.That(await s.Call(null, s.RunnerOne, Machine(op, s.RunnerOne, name)), Is.EqualTo(403), "no viewer token, as a schedule would have");
        Assert.That(await s.Call(s.Tokens.Create(s.AppId, s.Other), s.RunnerOne, Machine(op, s.RunnerOne, name)), Is.EqualTo(403), "not an administrator");
        Assert.That(await s.Call(admin, s.RunnerTwo, Machine(op, s.RunnerTwo, name)), Is.EqualTo(403), "approved for runner-01 only");
        foreach (var wrong in new string?[] { null, "", name.ToUpperInvariant() == name ? name.ToLowerInvariant() : name.ToUpperInvariant(), name + " ", "yes" })
            Assert.That(await s.Call(admin, s.RunnerOne, Machine(op, s.RunnerOne, wrong)), Is.EqualTo(400), $"confirm '{wrong}'");
        Assert.That(s.Commands.Sent, Is.Empty, "no refused call reached a Runner");
        Assert.That(s.Audit.Events.Where(e => e.Metadata.Contains(op)), Is.Empty, "refused calls ran nothing");

        s.Audit.Events.Clear();
        Assert.That(await s.Call(admin, s.RunnerOne, Machine(op, s.RunnerOne, name)), Is.EqualTo(op == "reboot" ? 202 : 200));
        Assert.That(s.Commands.Sent.Single(), Is.EqualTo((s.RunnerOne, s.Admin, op)));
        Assert.That(s.Audit.Events.Single().Metadata, Does.Contain(op).And.Contain(s.Admin.ToString()).And.Contain(s.RunnerOne.ToString()));
    }
}
